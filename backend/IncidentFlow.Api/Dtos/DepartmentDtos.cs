// The shape of a department as the API sends it to the frontend. Note it
// does NOT include the Members list from the entity: if it did, asking for
// the list of departments would drag every employee along with it.
using IncidentFlow.Api.Entities;

namespace IncidentFlow.Api.Dtos;

// MemberCount is nullable because it is only worth counting on the
// departments list page - when a department is nested inside a user we just
// want its name, not an extra COUNT query per row.
public record DepartmentOut(int Id, string Code, string Name, string Kind, string Description, int? MemberCount = null)
{
    // A small factory so the mapping from entity -> DTO is written once and
    // reused by every endpoint, instead of being copy-pasted around.
    // Kind is sent as its plain text name ("PRODUCT_AREA") because that is
    // what the frontend's TypeScript union type expects.
    public static DepartmentOut From(Department department, int? memberCount = null) => new(
        department.Id,
        department.Code,
        department.Name,
        department.Kind.ToString(),
        department.Description,
        memberCount
    );
}
