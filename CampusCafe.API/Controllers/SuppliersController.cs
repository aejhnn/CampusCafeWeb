using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using Microsoft.Extensions.Configuration;

namespace CampusCafe.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SuppliersController : ControllerBase
    {
        private readonly string connectionString;

        public SuppliersController(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\Users\alcan\Downloads\Physical Data Model (GILO, PAMATIAN, VILLAFOR, VELASCO).accdb;";
        }

        [HttpGet]
        public IActionResult GetSuppliers()
        {
            var suppliers = new List<object>();

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string query = "SELECT SupplierID, CompanyName, ContactPerson, TelephoneNumber FROM Supplier";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                suppliers.Add(new
                                {
                                    SupplierID = reader["SupplierID"] != DBNull.Value ? Convert.ToInt32(reader["SupplierID"]) : 0,
                                    SupplierName = reader["CompanyName"]?.ToString() ?? "",
                                    ContactPerson = reader["ContactPerson"]?.ToString() ?? "",
                                    Phone = reader["TelephoneNumber"]?.ToString() ?? ""
                                });
                            }
                        }
                    }
                    return Ok(suppliers);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Error reading suppliers: {ex.Message}");
                }
            }
        }

        [HttpPost]
        public IActionResult AddSupplier([FromBody] SupplierCreateDto sup)
        {
            if (sup == null) return BadRequest("Invalid supplier data.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    int nextId = 1;
                    using (OleDbCommand maxCmd = new OleDbCommand("SELECT MAX(SupplierID) FROM Supplier", conn))
                    {
                        object res = maxCmd.ExecuteScalar();
                        if (res != null && res != DBNull.Value) nextId = Convert.ToInt32(res) + 1;
                    }

                    string query = "INSERT INTO Supplier (SupplierID, CompanyName, ContactPerson, TelephoneNumber) VALUES (?, ?, ?, ?)";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("?", nextId);
                        cmd.Parameters.AddWithValue("?", sup.SupplierName ?? "");
                        cmd.Parameters.AddWithValue("?", sup.ContactPerson ?? "");
                        cmd.Parameters.AddWithValue("?", sup.Phone ?? "");
                        cmd.ExecuteNonQuery();
                    }

                    return Ok(new { message = "Supplier saved successfully!" });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to insert supplier: {ex.Message}");
                }
            }
        }
    }

    public class SupplierCreateDto
    {
        public string SupplierName { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }
}