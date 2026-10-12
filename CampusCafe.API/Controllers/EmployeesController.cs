using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using Microsoft.Extensions.Configuration;

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
                ?? @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\Users\alcan\Downloads\Physical Data Model (GILO, PAMATIAN, VILLAFOR, VELASCO).accdb;";
        }

        [HttpGet]
        public IActionResult GetEmployees()
        {
            List<object> list = new List<object>();

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM Employee", conn))
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var emp = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                emp[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            }
                            list.Add(emp);
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

                    DataTable empSchema = conn.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object[] { null, null, "Employee", null });
                    HashSet<string> empCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataRow row in empSchema.Rows) empCols.Add(row["COLUMN_NAME"].ToString());

                    string nameCol = empCols.Contains("Name") ? "Name" : (empCols.Contains("EmployeeName") ? "EmployeeName" : "Name");
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
        public string Name { get; set; }
        public string Passcode { get; set; }
    }
}