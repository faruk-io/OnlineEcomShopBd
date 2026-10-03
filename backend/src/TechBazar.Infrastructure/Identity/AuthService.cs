using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TechBazar.Application.Auth;
using TechBazar.Application.Common;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Persistence;

namespace TechBazar.Infrastructure.Identity;

public sealed class AuthService(
    UserManager<ApplicationUser> users,
    ApplicationDbContext db,
    ITokenService tokens,
    IAccountService account,
    IOptions<JwtOptions> jwtOptions) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private const string InvalidCredentials = "Invalid email or password.";

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var email = request.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null)
            throw new ConflictException("An account with this email already exists.");

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = request.FullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            CreatedAt = DateTime.UtcNow,
        };

        var create = await users.CreateAsync(user, request.Password);
        if (!create.Succeeded) throw ToValidation(create);

        var role = await users.AddToRoleAsync(user, Roles.Customer);
        if (!role.Succeeded) throw ToValidation(role);

        await account.SendVerificationAsync(user.Id, ipAddress, ct);   // never throws: a mail problem must not break sign-up

        return await IssueAsync(user, ipAddress, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        // Same message for unknown user / wrong password / disabled: do not leak which accounts exist. The (deliberately slow) password
        // hash is computed on every path so response time does not reveal it either.
        if (user is null || !user.IsActive)
        {
            _ = users.PasswordHasher.HashPassword(new ApplicationUser(), request.Password);
            throw new AuthenticationFailedException(InvalidCredentials);
        }
        if (await users.IsLockedOutAsync(user)) throw new AuthenticationFailedException("Account temporarily locked. Try again later.");

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw new AuthenticationFailedException(InvalidCredentials);
        }

        await users.ResetAccessFailedCountAsync(user);
        return await IssueAsync(user, ipAddress, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) throw new AuthenticationFailedException("Invalid refresh token.");
        var hash = tokens.Hash(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                     ?? throw new AuthenticationFailedException("Invalid refresh token.");

        if (stored.IsRevoked)
        {
            // A rotated/revoked token was replayed: assume theft and kill every live session of this user.
            await RevokeAllAsync(stored.UserId, "Reuse of revoked refresh token detected", now, ct);
            throw new AuthenticationFailedException("Refresh token is no longer valid. Please sign in again.");
        }
        if (stored.IsExpired(now)) throw new AuthenticationFailedException("Refresh token expired. Please sign in again.");

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user))
            throw new AuthenticationFailedException("Account is not available.");

        var (newToken, newHash) = tokens.CreateRefreshToken();

        // Atomic compare-and-set so two concurrent refreshes with the same token cannot both win.
        var revoked = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedByTokenHash, newHash)
                .SetProperty(t => t.RevokedReason, "Rotated"), ct);
        if (revoked == 0)
        {
            await RevokeAllAsync(stored.UserId, "Concurrent reuse of refresh token detected", now, ct);
            throw new AuthenticationFailedException("Refresh token is no longer valid. Please sign in again.");
        }

        return await BuildResponseAsync(user, newToken, newHash, ipAddress, now, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = tokens.Hash(refreshToken);
        var now = DateTime.UtcNow;
        // Possession of the (unguessable, 512-bit) token is the credential, so this works after the access token expired.
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, "Logout"), ct);
    }

    public Task LogoutAllAsync(Guid userId, CancellationToken ct = default) =>
        RevokeAllAsync(userId, "Logout everywhere", DateTime.UtcNow, ct);

    public async Task<UserDto> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User not found.");
        return new UserDto(user.Id, user.Email!, user.FullName, user.PhoneNumber, [.. await users.GetRolesAsync(user)], user.EmailConfirmed);
    }

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User not found.");
        user.FullName = request.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) throw ToValidation(result);
        return new UserDto(user.Id, user.Email!, user.FullName, user.PhoneNumber, [.. await users.GetRolesAsync(user)], user.EmailConfirmed);
    }

    private async Task<AuthResponse> IssueAsync(ApplicationUser user, string? ip, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Opportunistic housekeeping: drop tokens that expired more than 30 days ago.
        var cutoff = now.AddDays(-30);
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);

        var (token, hash) = tokens.CreateRefreshToken();
        return await BuildResponseAsync(user, token, hash, ip, now, ct);
    }

    private async Task<AuthResponse> BuildResponseAsync(ApplicationUser user, string refreshToken, string refreshHash, string? ip, DateTime now, CancellationToken ct)
    {
        var refreshExpires = now.AddDays(_jwt.RefreshTokenDays);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id, TokenHash = refreshHash, CreatedAt = now, ExpiresAt = refreshExpires, CreatedByIp = ip,
        });
        await db.SaveChangesAsync(ct);

        var roles = (await users.GetRolesAsync(user)).ToList();
        var access = tokens.CreateAccessToken(user, roles);
        return new AuthResponse(access.Value, access.ExpiresAt, refreshToken, refreshExpires,
            new UserDto(user.Id, user.Email!, user.FullName, user.PhoneNumber, roles, user.EmailConfirmed));
    }

    private Task<int> RevokeAllAsync(Guid userId, string reason, DateTime now, CancellationToken ct) =>
        db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), ct);

    private static ValidationException ToValidation(IdentityResult result) =>
        new(result.Errors.Select(e => new ValidationFailure(e.Code.Contains("Password") ? "Password" : e.Code.Contains("Email") ? "Email" : "", e.Description)));
}
