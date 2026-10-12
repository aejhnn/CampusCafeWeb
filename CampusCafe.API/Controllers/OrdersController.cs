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

            if (string.IsNullOrWhiteSpace(orderDto.CustomerName))
                return BadRequest("Customer name is required.");

            using (OleDbConnection conn = new OleDbConnection(connectionString))
            {
                try
                {
                    conn.Open();

                    // 1. Sync Customer Name into Customer Table
                    int customerId = 1;
                    string cleanName = orderDto.CustomerName.Trim();

                    using (OleDbCommand findCustCmd = new OleDbCommand("SELECT CustomerID FROM Customer WHERE CustomerName = ?", conn))
                    {
                        findCustCmd.Parameters.AddWithValue("?", cleanName);
                        object existingId = findCustCmd.ExecuteScalar();

                        if (existingId != null && existingId != DBNull.Value)
                        {
                            customerId = Convert.ToInt32(existingId);
                        }
                        else
                        {
                            using (OleDbCommand maxCustCmd = new OleDbCommand("SELECT MAX(CustomerID) FROM Customer", conn))
                            {
                                object maxRes = maxCustCmd.ExecuteScalar();
                                customerId = (maxRes != null && maxRes != DBNull.Value) ? Convert.ToInt32(maxRes) + 1 : 1;
                            }

                            string insertCustQuery = "INSERT INTO Customer (CustomerID, CustomerName) VALUES (?, ?)";
                            using (OleDbCommand insertCustCmd = new OleDbCommand(insertCustQuery, conn))
                            {
                                insertCustCmd.Parameters.AddWithValue("?", customerId);
                                insertCustCmd.Parameters.AddWithValue("?", cleanName);
                                insertCustCmd.ExecuteNonQuery();
                            }
                        }
                    }

                    // 2. Get Next OrderID
                    int nextOrderId = 1;
                    using (OleDbCommand maxCmd = new OleDbCommand("SELECT MAX(OrderID) FROM [Order]", conn))
                    {
                        object result = maxCmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value) nextOrderId = Convert.ToInt32(result) + 1;
                    }

                    int employeeId = orderDto.EmployeeID > 0 ? orderDto.EmployeeID : 1;

                    // 3. Build summary string for ProductDetails column
                    List<string> itemSummaries = new List<string>();
                    foreach (var item in orderDto.Items)
                    {
                        string pName = "Product #" + item.ProductID;
                        using (OleDbCommand pCmd = new OleDbCommand("SELECT ProductName FROM Product WHERE ProductID = ?", conn))
                        {
                            pCmd.Parameters.AddWithValue("?", item.ProductID);
                            object res = pCmd.ExecuteScalar();
                            if (res != null && res != DBNull.Value) pName = res.ToString();
                        }
                        itemSummaries.Add($"{pName} (x{item.Quantity})");
                    }
                    string productDetailsSummary = string.Join(", ", itemSummaries);

                    // 4. Insert into [Order] table with linked CustomerID & ProductDetails
                    string insertOrderQuery = "INSERT INTO [Order] (OrderID, CustomerID, EmployeeID, ProductDetails, OrderDateTime, Status, TotalAmount) VALUES (?, ?, ?, ?, ?, ?, ?)";
                    using (OleDbCommand cmd = new OleDbCommand(insertOrderQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("?", nextOrderId);
                        cmd.Parameters.AddWithValue("?", customerId);
                        cmd.Parameters.AddWithValue("?", employeeId);
                        cmd.Parameters.AddWithValue("?", productDetailsSummary);
                        cmd.Parameters.AddWithValue("?", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("?", "Pending");
                        cmd.Parameters.AddWithValue("?", orderDto.TotalAmount);
                        cmd.ExecuteNonQuery();
                    }

                    // 5. Insert into Order_Item table
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

                    return Ok(new { message = "Order placed successfully!", orderId = nextOrderId, customerName = cleanName });
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
        public string CustomerName { get; set; } = "Guest";
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