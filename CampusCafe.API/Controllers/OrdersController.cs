using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Linq;
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

                    // 1. Sync Customer Table safely with full dynamic column matching
                    int customerId = 1;
                    string cleanName = orderDto.CustomerName.Trim();
                    string customerType = string.IsNullOrWhiteSpace(orderDto.CustomerType) ? "Student" : orderDto.CustomerType.Trim();

                    DataTable custSchema = conn.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object[] { null, null, "Customer", null });
                    HashSet<string> custCols = GetColumnNames(custSchema);

                    string custIdCol = custCols.Contains("CustomerID") ? "CustomerID" : "ID";
                    string custNameCol = custCols.Contains("CustomerName") ? "CustomerName" : (custCols.Contains("Name") ? "Name" : "CustomerName");

                    using (OleDbCommand findCustCmd = new OleDbCommand($"SELECT [{custIdCol}] FROM Customer WHERE [{custNameCol}] = ?", conn))
                    {
                        findCustCmd.Parameters.AddWithValue("?", cleanName);
                        object existingId = findCustCmd.ExecuteScalar();

                        if (existingId != null && existingId != DBNull.Value)
                        {
                            customerId = Convert.ToInt32(existingId);
                        }
                        else
                        {
                            using (OleDbCommand maxCustCmd = new OleDbCommand($"SELECT MAX([{custIdCol}]) FROM Customer", conn))
                            {
                                object maxRes = maxCustCmd.ExecuteScalar();
                                customerId = (maxRes != null && maxRes != DBNull.Value) ? Convert.ToInt32(maxRes) + 1 : 1;
                            }

                            List<string> custInsertCols = new List<string> { $"[{custIdCol}]", $"[{custNameCol}]" };
                            List<OleDbParameter> custInsertParams = new List<OleDbParameter>
                            {
                                new OleDbParameter("?", customerId),
                                new OleDbParameter("?", cleanName)
                            };

                            // Dynamic fixes for all required customer table variations
                            if (custCols.Contains("CustomerType"))
                            {
                                custInsertCols.Add("[CustomerType]");
                                custInsertParams.Add(new OleDbParameter("?", customerType));
                            }
                            if (custCols.Contains("UniversityIDNumber"))
                            {
                                custInsertCols.Add("[UniversityIDNumber]");
                                custInsertParams.Add(new OleDbParameter("?", "N/A"));
                            }
                            if (custCols.Contains("ContactNumber") || custCols.Contains("ContactNum") || custCols.Contains("Phone"))
                            {
                                string phoneCol = custCols.Contains("ContactNumber") ? "ContactNumber" : (custCols.Contains("ContactNum") ? "ContactNum" : "Phone");
                                custInsertCols.Add($"[{phoneCol}]");
                                custInsertParams.Add(new OleDbParameter("?", "N/A"));
                            }

                            string custPlaceholders = string.Join(", ", Enumerable.Repeat("?", custInsertCols.Count));
                            string insertCustQuery = $"INSERT INTO Customer ({string.Join(", ", custInsertCols)}) VALUES ({custPlaceholders})";
                            
                            using (OleDbCommand insertCustCmd = new OleDbCommand(insertCustQuery, conn))
                            {
                                foreach (var p in custInsertParams) insertCustCmd.Parameters.Add(p);
                                insertCustCmd.ExecuteNonQuery();
                            }
                        }
                    }

                    // 2. Get Next OrderID
                    DataTable orderSchema = conn.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object[] { null, null, "Order", null });
                    HashSet<string> orderCols = GetColumnNames(orderSchema);
                    string orderIdCol = orderCols.Contains("OrderID") ? "OrderID" : "ID";

                    int nextOrderId = 1;
                    using (OleDbCommand maxCmd = new OleDbCommand($"SELECT MAX([{orderIdCol}]) FROM [Order]", conn))
                    {
                        object result = maxCmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value) nextOrderId = Convert.ToInt32(result) + 1;
                    }

                    int employeeId = orderDto.EmployeeID > 0 ? orderDto.EmployeeID : 1;

                    // 3. Build item summary string
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

                    // 4. Dynamic Column Match for [Order] table
                    List<string> cols = new List<string>();
                    List<OleDbParameter> paramsList = new List<OleDbParameter>();

                    AddColumnIfPresent(orderCols, cols, paramsList, orderIdCol, nextOrderId);
                    AddColumnIfPresent(orderCols, cols, paramsList, "CustomerID", customerId);
                    AddColumnIfPresent(orderCols, cols, paramsList, "EmployeeID", employeeId);

                    if (orderCols.Contains("ProductDetails")) AddColumnIfPresent(orderCols, cols, paramsList, "ProductDetails", productDetailsSummary);
                    if (orderCols.Contains("OrderDateTime")) AddColumnIfPresent(orderCols, cols, paramsList, "OrderDateTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    else if (orderCols.Contains("OrderDate")) AddColumnIfPresent(orderCols, cols, paramsList, "OrderDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    if (orderCols.Contains("Status")) AddColumnIfPresent(orderCols, cols, paramsList, "Status", "Pending");

                    if (orderCols.Contains("TotalAmount")) AddColumnIfPresent(orderCols, cols, paramsList, "TotalAmount", orderDto.TotalAmount);
                    else if (orderCols.Contains("Total")) AddColumnIfPresent(orderCols, cols, paramsList, "Total", orderDto.TotalAmount);

                    string orderPlaceholders = string.Join(", ", Enumerable.Repeat("?", cols.Count));
                    string insertOrderSql = $"INSERT INTO [Order] ({string.Join(", ", cols)}) VALUES ({orderPlaceholders})";

                    using (OleDbCommand cmd = new OleDbCommand(insertOrderSql, conn))
                    {
                        foreach (var p in paramsList) cmd.Parameters.Add(p);
                        cmd.ExecuteNonQuery();
                    }

                    // 5. Dynamic Column Match for Order_Item table
                    DataTable itemSchema = conn.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object[] { null, null, "Order_Item", null });
                    HashSet<string> itemCols = GetColumnNames(itemSchema);
                    string orderItemIdCol = itemCols.Contains("OrderItemID") ? "OrderItemID" : "ID";

                    foreach (var item in orderDto.Items)
                    {
                        int nextOrderItemId = 1;
                        using (OleDbCommand maxItemCmd = new OleDbCommand($"SELECT MAX([{orderItemIdCol}]) FROM Order_Item", conn))
                        {
                            object itemRes = maxItemCmd.ExecuteScalar();
                            if (itemRes != null && itemRes != DBNull.Value) nextOrderItemId = Convert.ToInt32(itemRes) + 1;
                        }

                        List<string> itemColsList = new List<string>();
                        List<OleDbParameter> itemParamsList = new List<OleDbParameter>();

                        AddColumnIfPresent(itemCols, itemColsList, itemParamsList, orderItemIdCol, nextOrderItemId);
                        AddColumnIfPresent(itemCols, itemColsList, itemParamsList, orderIdCol, nextOrderId);
                        AddColumnIfPresent(itemCols, itemColsList, itemParamsList, "ProductID", item.ProductID);
                        AddColumnIfPresent(itemCols, itemColsList, itemParamsList, "Quantity", item.Quantity);

                        if (itemCols.Contains("UnitSellingPrice")) AddColumnIfPresent(itemCols, itemColsList, itemParamsList, "UnitSellingPrice", item.UnitPrice);
                        else if (itemCols.Contains("UnitPrice")) AddColumnIfPresent(itemCols, itemColsList, itemParamsList, "UnitPrice", item.UnitPrice);

                        string itemPlaceholders = string.Join(", ", Enumerable.Repeat("?", itemColsList.Count));
                        string insertItemSql = $"INSERT INTO Order_Item ({string.Join(", ", itemColsList)}) VALUES ({itemPlaceholders})";

                        using (OleDbCommand itemCmd = new OleDbCommand(insertItemSql, conn))
                        {
                            foreach (var p in itemParamsList) itemCmd.Parameters.Add(p);
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

        private HashSet<string> GetColumnNames(DataTable schemaTable)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (schemaTable != null)
            {
                foreach (DataRow row in schemaTable.Rows)
                {
                    set.Add(row["COLUMN_NAME"].ToString());
                }
            }
            return set;
        }

        private void AddColumnIfPresent(HashSet<string> schemaCols, List<string> colList, List<OleDbParameter> paramList, string colName, object val)
        {
            if (schemaCols.Contains(colName))
            {
                colList.Add($"[{colName}]");
                paramList.Add(new OleDbParameter("?", val ?? DBNull.Value));
            }
        }
    }

    public class OrderCreateDto
    {
        public string CustomerName { get; set; } = "Guest";
        public string CustomerType { get; set; } = "Student";
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