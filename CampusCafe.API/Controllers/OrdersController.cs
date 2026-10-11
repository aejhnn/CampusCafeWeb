using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using Microsoft.Extensions.Configuration;

namespace CampusCafe.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly string connectionString;

        public OrdersController(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\Users\alcan\Downloads\Physical Data Model (GILO, PAMATIAN, VILLAFOR, VELASCO).accdb;";
        }

        [HttpPost]
        public IActionResult PlaceOrder([FromBody] OrderCreateDto orderDto)
        {
            if (orderDto == null || orderDto.Items == null || orderDto.Items.Count == 0)
                return BadRequest("Order items cannot be empty.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();

                    // 1. Get Next OrderID
                    int nextOrderId = 1;
                    using (OleDbCommand maxCmd = new OleDbCommand("SELECT MAX(OrderID) FROM [Order]", conn))
                    {
                        object result = maxCmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value) nextOrderId = Convert.ToInt32(result) + 1;
                    }

                    int customerId = orderDto.CustomerID > 0 ? orderDto.CustomerID : 1;
                    int employeeId = orderDto.EmployeeID > 0 ? orderDto.EmployeeID : 1;

                    // 2. Insert into Order Table
                    string insertOrderQuery = "INSERT INTO [Order] (OrderID, CustomerID, EmployeeID, OrderDateTime, Status, TotalAmount) VALUES (?, ?, ?, ?, ?, ?)";
                    using (OleDbCommand cmd = new OleDbCommand(insertOrderQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("?", nextOrderId);
                        cmd.Parameters.AddWithValue("?", customerId);
                        cmd.Parameters.AddWithValue("?", employeeId);
                        cmd.Parameters.AddWithValue("?", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("?", "Pending");
                        cmd.Parameters.AddWithValue("?", orderDto.TotalAmount);
                        cmd.ExecuteNonQuery();
                    }

                    // 3. Insert into Order_Item Table
                    foreach (var item in orderDto.Items)
                    {
                        int nextOrderItemId = 1;
                        using (OleDbCommand maxItemCmd = new OleDbCommand("SELECT MAX(OrderItemID) FROM Order_Item", conn))
                        {
                            object itemRes = maxItemCmd.ExecuteScalar();
                            if (itemRes != null && itemRes != DBNull.Value) nextOrderItemId = Convert.ToInt32(itemRes) + 1;
                        }

                        string insertItemQuery = "INSERT INTO Order_Item (OrderItemID, OrderID, ProductID, Quantity, UnitSellingPrice) VALUES (?, ?, ?, ?, ?)";
                        using (OleDbCommand itemCmd = new OleDbCommand(insertItemQuery, conn))
                        {
                            itemCmd.Parameters.AddWithValue("?", nextOrderItemId);
                            itemCmd.Parameters.AddWithValue("?", nextOrderId);
                            itemCmd.Parameters.AddWithValue("?", item.ProductID);
                            itemCmd.Parameters.AddWithValue("?", item.Quantity);
                            itemCmd.Parameters.AddWithValue("?", item.UnitPrice);
                            itemCmd.ExecuteNonQuery();
                        }
                    }

                    return Ok(new { message = "Order placed successfully!", orderId = nextOrderId });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to process order: {ex.Message}");
                }
            }
        }
    }

    public class OrderCreateDto
    {
        public int CustomerID { get; set; } = 1;
        public int EmployeeID { get; set; } = 1;
        public decimal TotalAmount { get; set; }
        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
    }

    public class OrderItemDto
    {
        public int ProductID { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}