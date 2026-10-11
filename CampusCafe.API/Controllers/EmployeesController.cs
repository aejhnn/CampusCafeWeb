using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
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
            var employees = new List<object>();

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string query = "SELECT EmployeeID, Name, ContactNumber, WorkSchedule FROM Employee";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                employees.Add(new
                                {
                                    EmployeeID = reader["EmployeeID"] != DBNull.Value ? Convert.ToInt32(reader["EmployeeID"]) : 0,
                                    Name = reader["Name"]?.ToString() ?? "",
                                    ContactNumber = reader["ContactNumber"]?.ToString() ?? "",
                                    WorkSchedule = reader["WorkSchedule"]?.ToString() ?? "Full-Time"
                                });
                            }
                        }
                    }
                    return Ok(employees);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Error reading employees: {ex.Message}");
                }
            }
        }

        [HttpPost]
        public IActionResult AddEmployee([FromBody] EmployeeCreateDto emp)
        {
            if (emp == null) return BadRequest("Invalid employee data.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    int nextId = 1;
                    using (OleDbCommand maxCmd = new OleDbCommand("SELECT MAX(EmployeeID) FROM Employee", conn))
                    {
                        object res = maxCmd.ExecuteScalar();
                        if (res != null && res != DBNull.Value) nextId = Convert.ToInt32(res) + 1;
                    }

                    string query = "INSERT INTO Employee (EmployeeID, Name, ContactNumber, WorkSchedule) VALUES (?, ?, ?, ?)";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("?", nextId);
                        cmd.Parameters.AddWithValue("?", emp.Name ?? "");
                        cmd.Parameters.AddWithValue("?", emp.ContactNumber ?? "");
                        cmd.Parameters.AddWithValue("?", emp.WorkSchedule ?? "Full-Time");
                        cmd.ExecuteNonQuery();
                    }

                    return Ok(new { message = "Employee saved successfully!" });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to insert employee: {ex.Message}");
                }
            }
        }
    }

    public class EmployeeCreateDto
    {
        public string Name { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string WorkSchedule { get; set; } = "Full-Time";
    }
}