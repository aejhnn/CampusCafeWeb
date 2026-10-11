using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using Microsoft.Extensions.Configuration;

namespace CampusCafe.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly string connectionString;

        public ProductsController(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\Users\alcan\Downloads\Physical Data Model (GILO, PAMATIAN, VILLAFOR, VELASCO).accdb;";
        }

        [HttpGet]
        public IActionResult GetProducts()
        {
            var products = new List<object>();

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string query = "SELECT ProductID, ProductName, Description, Price, Category, AvailabilityStatus FROM Product";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                products.Add(new
                                {
                                    ProductID = reader["ProductID"] != DBNull.Value ? Convert.ToInt32(reader["ProductID"]) : 0,
                                    ProductName = reader["ProductName"]?.ToString() ?? "",
                                    Description = reader["Description"]?.ToString() ?? "",
                                    Price = reader["Price"] != DBNull.Value ? Convert.ToDecimal(reader["Price"]) : 0m,
                                    Category = reader["Category"]?.ToString() ?? "",
                                    AvailabilityStatus = reader["AvailabilityStatus"]?.ToString() ?? "Available"
                                });
                            }
                        }
                    }
                    return Ok(products);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Database Read Error: {ex.Message}");
                }
            }
        }

        [HttpPost]
        public IActionResult AddProduct([FromBody] ProductCreateDto product)
        {
            if (product == null) return BadRequest("Invalid product data.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();

                    int nextId = 1;
                    using (OleDbCommand maxCmd = new OleDbCommand("SELECT MAX(ProductID) FROM Product", conn))
                    {
                        object result = maxCmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                        {
                            nextId = Convert.ToInt32(result) + 1;
                        }
                    }

                    string insertQuery = "INSERT INTO Product (ProductID, ProductName, Description, Price, Category, AvailabilityStatus) VALUES (?, ?, ?, ?, ?, ?)";
                    
                    using (OleDbCommand cmd = new OleDbCommand(insertQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("?", nextId);
                        cmd.Parameters.AddWithValue("?", product.ProductName ?? "");
                        cmd.Parameters.AddWithValue("?", product.Description ?? "");
                        cmd.Parameters.AddWithValue("?", product.Price);
                        cmd.Parameters.AddWithValue("?", product.Category ?? "Coffee");
                        cmd.Parameters.AddWithValue("?", product.AvailabilityStatus ?? "Available");

                        cmd.ExecuteNonQuery();
                    }

                    return Ok(new { message = "Product added successfully!" });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to insert product: {ex.Message}");
                }
            }
        }

        [HttpPut("{id}/toggle-status")]
        public IActionResult ToggleStatus(int id, [FromBody] StatusUpdateDto statusDto)
        {
            if (statusDto == null) return BadRequest("Invalid status data.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string query = "UPDATE Product SET AvailabilityStatus = ? WHERE ProductID = ?";
                    using (OleDbCommand cmd = new OleDbCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("?", statusDto.AvailabilityStatus ?? "Available");
                        cmd.Parameters.AddWithValue("?", id);
                        cmd.ExecuteNonQuery();
                    }
                    return Ok(new { message = "Status updated successfully!" });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to update status: {ex.Message}");
                }
            }
        }
    }

    public class ProductCreateDto
    {
        public string ProductName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Category { get; set; } = string.Empty;
        public string AvailabilityStatus { get; set; } = "Available";
    }

    public class StatusUpdateDto
    {
        public string AvailabilityStatus { get; set; } = "Available";
    }
}