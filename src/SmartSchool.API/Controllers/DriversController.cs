using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class DriversController : BaseApiController
{
    private readonly IUnitOfWork _uow;

    public DriversController(IUnitOfWork uow) => _uow = uow;

    [HttpGet]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<PagedResult<DriverListItem>>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _uow.Drivers.Query().AsQueryable();
        if (!includeInactive)
            query = query.Where(d => d.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(d =>
                EF.Functions.Like(d.FullName, pattern) ||
                EF.Functions.Like(d.Phone, pattern) ||
                EF.Functions.Like(d.LicenseNo, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(d => d.FullName).ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new DriverListItem(
                d.Id, d.FullName, d.Phone, d.LicenseNo,
                d.UserId, d.User.Username, d.IsActive,
                d.AssignedBus != null ? d.AssignedBus.Id : null,
                d.AssignedBus != null ? d.AssignedBus.BusNumber : null))
            .ToListAsync(ct);

        return OkEnvelope(PagedResult<DriverListItem>.Create(items, page, pageSize, total));
    }

    /// <summary>Create a driver together with the mobile-app login (one call, one transaction).</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<DriverListItem>>> Create(DriverCreateRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        if (await _uow.Users.AnyAsync(u => u.Username == username, ct))
            return ConflictEnvelope($"Username '{username}' is already taken.");
        if (await _uow.Drivers.AnyAsync(d => d.LicenseNo == request.LicenseNo.Trim(), ct))
            return ConflictEnvelope($"License number '{request.LicenseNo}' is already registered.");

        var user = new User
        {
            Username = username,
            FullName = request.FullName.Trim(),
            Role = UserRole.Driver,
            PasswordHash = PasswordHasher.Hash(request.Password),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var driver = new Driver
        {
            FullName = request.FullName.Trim(),
            Phone = request.Phone.Trim(),
            LicenseNo = request.LicenseNo.Trim(),
            User = user,
            IsActive = true
        };
        await _uow.Drivers.AddAsync(driver, ct);
        await _uow.SaveChangesAsync(ct);

        return CreatedEnvelope(new DriverListItem(
            driver.Id, driver.FullName, driver.Phone, driver.LicenseNo,
            user.Id, user.Username, driver.IsActive, null, null),
            "Driver created successfully.");
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Update(int id, DriverUpdateRequest request, CancellationToken ct)
    {
        var driver = await _uow.Drivers.Query(false).Include(d => d.User).Include(d => d.AssignedBus)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (driver is null)
            return NotFoundEnvelope($"Driver {id} was not found.");

        var license = request.LicenseNo.Trim();
        if (await _uow.Drivers.AnyAsync(d => d.Id != id && d.LicenseNo == license, ct))
            return ConflictEnvelope($"License number '{license}' is already registered.");

        driver.FullName = request.FullName.Trim();
        driver.Phone = request.Phone.Trim();
        driver.LicenseNo = license;
        driver.IsActive = request.IsActive;
        // Login account bhi driver ke saath chalega: band driver app mein login nahi kar sakta.
        if (driver.User is not null)
        {
            driver.User.FullName = driver.FullName;
            driver.User.IsActive = driver.IsActive;
        }
        // Band driver kisi bus par nahi rehta.
        if (!driver.IsActive && driver.AssignedBus is not null)
            driver.AssignedBus.DriverId = null;

        await _uow.SaveChangesAsync(ct);
        return OkMessage("Driver updated successfully.");
    }

    /// <summary>Driver ka app login badalna (username) ya naya password dena. Password khali ho to purana rehta hai.</summary>
    [HttpPut("{id:int}/login")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> UpdateLogin(int id, DriverLoginRequest request, CancellationToken ct)
    {
        var driver = await _uow.Drivers.Query(false).Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (driver is null || driver.User is null)
            return NotFoundEnvelope($"Driver {id} was not found.");

        var username = (request.Username ?? string.Empty).Trim();
        if (username.Length < 3 || username.Length > 50)
            return BadRequestEnvelope("Username 3 se 50 characters ka hona chahiye.");
        if (await _uow.Users.AnyAsync(u => u.Username == username && u.Id != driver.UserId, ct))
            return ConflictEnvelope($"Username '{username}' is already taken.");

        var newPassword = request.NewPassword ?? string.Empty;
        if (newPassword.Length > 0 && newPassword.Length < 6)
            return BadRequestEnvelope("Naya password kam az kam 6 characters ka hona chahiye.");

        driver.User.Username = username;
        if (newPassword.Length > 0)
            driver.User.PasswordHash = PasswordHasher.Hash(newPassword);
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Driver app login updated.");
    }

    /// <summary>Soft delete: unassigns the bus, keeps attendance history intact.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Deactivate(int id, CancellationToken ct)
    {
        var driver = await _uow.Drivers.Query(false).Include(d => d.AssignedBus).Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (driver is null)
            return NotFoundEnvelope($"Driver {id} was not found.");

        driver.IsActive = false;
        if (driver.User is not null) driver.User.IsActive = false; // app login bhi band
        if (driver.AssignedBus is not null)
            driver.AssignedBus.DriverId = null;
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Driver deactivated successfully.");
    }
}
