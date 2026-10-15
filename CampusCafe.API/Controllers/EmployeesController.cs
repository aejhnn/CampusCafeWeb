using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Data.OleDb;

namespace CampusCafe.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeesController : ControllerBase
    {
        private readonly string connectionString;

        public EmployeesController(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? DbHelper.FallbackConnection;
        }

        // The Employee table stores the schedule as text: exactly "Full-Time" or "Part-Time".
        private static string? NormalizeSchedule(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string t = value.Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
            if (t == "fulltime") return "Full-Time";
            if (t == "parttime") return "Part-Time";
            return null;
        }

        private static string Text(OleDbDataReader reader, string? column)
        {
            if (column == null) return "";
            object v = reader[column];
            return v == DBNull.Value ? "" : (v.ToString() ?? "");
        }

        // GET api/Employees
        [HttpGet]
        public IActionResult GetEmployees()
        {
            var list = new List<object>();

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    var cols = DbHelper.GetColumns(conn, "Employee");
                    DbHelper.RequireTable(cols, "Employee");

                    string idCol = DbHelper.FirstExisting(cols, "EmployeeID", "ID") ?? "EmployeeID";
                    string? nameCol = DbHelper.FirstExisting(cols, "Name", "Employee_Name", "EmployeeName");
                    string? phoneCol = DbHelper.FirstExisting(cols, "ContactNumber");
                    string? schedCol = DbHelper.FirstExisting(cols, "WorkSchedule");
                    string? posCol = DbHelper.FirstExisting(cols, "Position");

                    var wanted = new[] { idCol, nameCol, phoneCol, schedCol, posCol }
                        .Where(c => c != null).Select(c => $"[{c}]");
                    string sql = $"SELECT {string.Join(", ", wanted)} FROM [Employee] ORDER BY [{idCol}]";

                    using (var cmd = new OleDbCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new
                            {
                                employeeID = Convert.ToInt32(reader[idCol]),
                                name = Text(reader, nameCol),
                                contactNumber = Text(reader, phoneCol),
                                position = Text(reader, posCol),
                                workSchedule = Text(reader, schedCol)
                            });
                        }
                    }
                    return Ok(list);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Error reading employees: {ex.Message}");
                }
            }
        }

        // POST api/Employees  -> add a new employee (the "+" form)
        [HttpPost]
        public IActionResult AddEmployee([FromBody] EmployeeSaveDto dto)
        {
            if (dto == null) return BadRequest("Invalid employee data.");

            string name = (dto.Name ?? "").Trim();
            string contact = string.IsNullOrWhiteSpace(dto.ContactNumber) ? "N/A" : dto.ContactNumber.Trim();
            string position = string.IsNullOrWhiteSpace(dto.Position) ? "Staff" : dto.Position.Trim();
            string? schedule = NormalizeSchedule(dto.WorkSchedule);

            if (name.Length == 0) return BadRequest("Employee name is required.");
            if (name.Length > 50) return BadRequest("Name is too long (max 50 characters).");
            if (contact.Length > 50) return BadRequest("Contact number is too long (max 50 characters).");
            if (position.Length > 50) return BadRequest("Role / position is too long (max 50 characters).");
            if (schedule == null) return BadRequest("Please choose a schedule: Full-Time or Part-Time.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                OleDbTransaction? tx = null;
                try
                {
                    conn.Open();
                    var cols = DbHelper.GetColumns(conn, "Employee");
                    DbHelper.RequireTable(cols, "Employee");
                    DbHelper.EnsureColumn(conn, cols, "Employee", "Position", "TEXT(50)");

                    string idCol = DbHelper.FirstExisting(cols, "EmployeeID", "ID") ?? "EmployeeID";
                    string nameCol = DbHelper.FirstExisting(cols, "Name", "Employee_Name", "EmployeeName") ?? "Name";

                    if (DbHelper.Exists(conn, null, $"SELECT COUNT(*) FROM [Employee] WHERE [{nameCol}] = ?", name))
                        return Conflict($"An employee named '{name}' already exists.");

                    tx = conn.BeginTransaction();
                    int newId = DbHelper.NextId(conn, tx, "Employee", idCol);
                    DbHelper.Insert(conn, tx, "Employee", cols,
                        (idCol, newId),
                        (nameCol, name),
                        ("ContactNumber", contact),
                        ("WorkSchedule", schedule),
                        ("Position", position));
                    tx.Commit();

                    return Ok(new
                    {
                        employeeID = newId,
                        name,
                        contactNumber = contact,
                        position,
                        workSchedule = schedule
                    });
                }
                catch (Exception ex)
                {
                    DbHelper.SafeRollback(tx);
                    return StatusCode(500, $"Failed to add employee: {ex.Message}");
                }
            }
        }

        // PUT api/Employees/5  -> change role / position and schedule (only the fields that are sent)
        [HttpPut("{id:int}")]
        public IActionResult UpdateEmployee(int id, [FromBody] EmployeeSaveDto dto)
        {
            if (dto == null) return BadRequest("Invalid employee data.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    var cols = DbHelper.GetColumns(conn, "Employee");
                    DbHelper.RequireTable(cols, "Employee");

                    string idCol = DbHelper.FirstExisting(cols, "EmployeeID", "ID") ?? "EmployeeID";
                    string nameCol = DbHelper.FirstExisting(cols, "Name", "Employee_Name", "EmployeeName") ?? "Name";

                    var sets = new List<string>();
                    var args = new List<object?>();

                    if (dto.Position != null)
                    {
                        string position = dto.Position.Trim();
                        if (position.Length == 0) return BadRequest("Role / position cannot be empty.");
                        if (position.Length > 50) return BadRequest("Role / position is too long (max 50 characters).");
                        DbHelper.EnsureColumn(conn, cols, "Employee", "Position", "TEXT(50)");
                        sets.Add("[Position] = ?"); args.Add(position);
                    }
                    if (dto.WorkSchedule != null)
                    {
                        string? schedule = NormalizeSchedule(dto.WorkSchedule);
                        if (schedule == null) return BadRequest("Schedule must be Full-Time or Part-Time.");
                        sets.Add("[WorkSchedule] = ?"); args.Add(schedule);
                    }
                    if (dto.Name != null)
                    {
                        string name = dto.Name.Trim();
                        if (name.Length == 0) return BadRequest("Employee name cannot be empty.");
                        if (name.Length > 50) return BadRequest("Name is too long (max 50 characters).");
                        sets.Add($"[{nameCol}] = ?"); args.Add(name);
                    }
                    if (dto.ContactNumber != null)
                    {
                        string contact = dto.ContactNumber.Trim();
                        if (contact.Length == 0) contact = "N/A";
                        if (contact.Length > 50) return BadRequest("Contact number is too long (max 50 characters).");
                        sets.Add("[ContactNumber] = ?"); args.Add(contact);
                    }

                    if (sets.Count == 0) return BadRequest("Nothing to update.");

                    if (!DbHelper.Exists(conn, null, $"SELECT COUNT(*) FROM [Employee] WHERE [{idCol}] = ?", id))
                        return NotFound("Employee not found.");

                    args.Add(id);
                    DbHelper.NonQuery(conn, null,
                        $"UPDATE [Employee] SET {string.Join(", ", sets)} WHERE [{idCol}] = ?", args.ToArray());

                    return Ok(new { message = "Employee updated.", employeeID = id });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to update employee: {ex.Message}");
                }
            }
        }

        // DELETE api/Employees/5  -> the "-" button
        [HttpDelete("{id:int}")]
        public IActionResult DeleteEmployee(int id)
        {
            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    var cols = DbHelper.GetColumns(conn, "Employee");
                    DbHelper.RequireTable(cols, "Employee");

                    string idCol = DbHelper.FirstExisting(cols, "EmployeeID", "ID") ?? "EmployeeID";
                    string nameCol = DbHelper.FirstExisting(cols, "Name", "Employee_Name", "EmployeeName") ?? "Name";

                    object? found = DbHelper.Scalar(conn, null, $"SELECT [{nameCol}] FROM [Employee] WHERE [{idCol}] = ?", id);
                    if (found == null || found == DBNull.Value) return NotFound("Employee not found.");

                    string name = found.ToString() ?? "";
                    if (name.Contains("kiosk", StringComparison.OrdinalIgnoreCase))
                        return BadRequest("Kiosk system accounts cannot be removed.");

                    // Orders keep the employee who took them, so an employee with orders must stay in the database.
                    if (DbHelper.Exists(conn, null, "SELECT COUNT(*) FROM [Order] WHERE EmployeeID = ?", id))
                        return Conflict($"{name} is linked to existing orders and cannot be removed. Change the role or schedule instead.");

                    DbHelper.NonQuery(conn, null, $"DELETE FROM [Employee] WHERE [{idCol}] = ?", id);
                    return Ok(new { message = $"{name} was removed." });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to remove employee: {ex.Message}");
                }
            }
        }

        [HttpPost("verify")]
        public IActionResult VerifyStaff([FromBody] StaffLoginDto login)
        {
            if (login == null || string.IsNullOrWhiteSpace(login.Name))
                return BadRequest("Staff Name is required.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();

                    DataTable empSchema = conn.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, "Employee", null })!;
                    HashSet<string> empCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataRow row in empSchema.Rows) empCols.Add(row["COLUMN_NAME"].ToString() ?? "");

                    string nameCol = empCols.Contains("Name") ? "Name" : (empCols.Contains("EmployeeName") ? "EmployeeName" : (empCols.Contains("Employee_Name") ? "Employee_Name" : "Name"));
                    string idCol = empCols.Contains("EmployeeID") ? "EmployeeID" : "ID";

                    // Query strictly matching Staff Name
                    string query = $"SELECT [{idCol}], [{nameCol}] FROM Employee WHERE [{nameCol}] LIKE ?";

                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        string input = login.Name.Trim();
                        cmd.Parameters.AddWithValue("?", "%" + input + "%");

                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                int empId = Convert.ToInt32(reader[idCol]);
                                string empName = reader[nameCol]?.ToString() ?? "Staff";

                                if (empName.Equals("Kiosk", StringComparison.OrdinalIgnoreCase))
                                {
                                    return Unauthorized("Kiosk system accounts cannot access Admin Dashboard.");
                                }

                                return Ok(new { success = true, employeeId = empId, name = empName });
                            }
                        }
                    }

                    return Unauthorized("Staff name not found in employee database.");
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Authentication error: {ex.Message}");
                }
            }
        }
    }

    public class StaffLoginDto
    {
        public string Name { get; set; } = string.Empty;
        public string Passcode { get; set; } = string.Empty;
    }

    // Every field is optional so the same shape works for add (all fields) and edit (only what changed).
    public class EmployeeSaveDto
    {
        public string? Name { get; set; }
        public string? ContactNumber { get; set; }
        public string? Position { get; set; }
        public string? WorkSchedule { get; set; }
    }
}
