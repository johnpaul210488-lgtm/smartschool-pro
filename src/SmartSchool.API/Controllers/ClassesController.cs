using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;

namespace SmartSchool.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ClassesController : BaseApiController
{
    private readonly IUnitOfWork _uow;

    public ClassesController(IUnitOfWork uow) => _uow = uow;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ClassListItem>>>> GetAll(CancellationToken ct)
    {
        var items = await _uow.Classes.Query()
            .Select(c => new ClassListItem(
                c.Id, c.Name, c.Section, c.Name + "-" + c.Section,
                c.TeacherId,
                c.ClassTeacher != null ? c.ClassTeacher.FullName : null,
                c.Students.Count(s => s.IsActive)))
            .ToListAsync(ct);

        var sorted = items
            .OrderBy(c => int.TryParse(c.Name, out var n) ? n : int.MaxValue)
            .ThenBy(c => c.Name).ThenBy(c => c.Section)
            .ToList();

        return OkEnvelope(sorted);
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<ClassListItem>>> Create(ClassUpsertRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        var section = request.Section.Trim().ToUpperInvariant();

        if (await _uow.Classes.AnyAsync(c => c.Name == name && c.Section == section, ct))
            return ConflictEnvelope($"Class {name}-{section} already exists.");
        if (request.TeacherId is not null && !await _uow.Teachers.AnyAsync(t => t.Id == request.TeacherId, ct))
            return BadRequestEnvelope($"Teacher {request.TeacherId} does not exist.");

        var entity = new SchoolClass { Name = name, Section = section, TeacherId = request.TeacherId };
        await _uow.Classes.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return CreatedEnvelope(
            new ClassListItem(entity.Id, entity.Name, entity.Section, $"{entity.Name}-{entity.Section}",
                entity.TeacherId, null, 0),
            "Class created successfully.");
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Update(int id, ClassUpsertRequest request, CancellationToken ct)
    {
        var entity = await _uow.Classes.FindAsync(new object[] { id }, ct);
        if (entity is null)
            return NotFoundEnvelope($"Class {id} was not found.");

        var name = request.Name.Trim();
        var section = request.Section.Trim().ToUpperInvariant();

        if (await _uow.Classes.AnyAsync(c => c.Id != id && c.Name == name && c.Section == section, ct))
            return ConflictEnvelope($"Class {name}-{section} already exists.");
        if (request.TeacherId is not null && !await _uow.Teachers.AnyAsync(t => t.Id == request.TeacherId, ct))
            return BadRequestEnvelope($"Teacher {request.TeacherId} does not exist.");

        entity.Name = name;
        entity.Section = section;
        entity.TeacherId = request.TeacherId;
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Class updated successfully.");
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        var entity = await _uow.Classes.FindAsync(new object[] { id }, ct);
        if (entity is null)
            return NotFoundEnvelope($"Class {id} was not found.");

        if (await _uow.Students.AnyAsync(s => s.ClassId == id, ct))
            return ConflictEnvelope("Cannot delete a class that has students. Move the students first.");

        _uow.Classes.Remove(entity);
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Class deleted successfully.");
    }
}
