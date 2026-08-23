using System;
using System.Data;
using System.Text;
using System.IO;
using MySqlConnector;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MRR.Data;
using MRR.Data.Entities;
using System.Xml.Serialization;

namespace MRR.Services
{
    /// <summary>
    /// DataService — Generic table access and SQL-shaped output. Moves to MRR.Admin in step 5 of
    /// API_DECOMPOSITION_DESIGN.md, where it gains reload-after-write, an audit log and
    /// loopback binding. Grouped here so that move is a file rename.
    ///
    /// Part of the step 4 split (API_DECOMPOSITION_DESIGN.md section 4): DataService is
    /// one class doing several jobs, so it is first separated by concern into partials.
    /// Splitting the file changes nothing semantically, but it makes each concern's real
    /// dependencies visible, which is what the repository extraction needs.
    /// </summary>
    public partial class DataService
    {



        ///////////////////////////////////////////////////////////////////////////
        // 
        ///////////////////////////////////////////////////////////////////////////

        // Return the results of any query as a JSON string (uses DataTable -> JSON)
        public string GetQueryResultsJson(string query, string name = "data")
        {
            var dt = GetQueryResults(query);
            // Serialize the DataTable rows as an array of objects under a dynamic property name
            var payload = new Dictionary<string, object> { { name, dt } };
            return JsonConvert.SerializeObject(payload);
        }

        // --- Legacy-style helpers (ported from Database.cs) ---
        // Provide backwards-compatible methods so existing code that used Database
        // can call similar APIs on DataService during the migration.


        public string GetHTMLfromQuery(string strSQL)
        {
            var dt = GetQueryResults(strSQL);
            var sb = new System.Text.StringBuilder();
            sb.Append("<table width='100%'>");
            // header row
            sb.Append("<tr>");
            foreach (DataColumn col in dt.Columns)
            {
                sb.Append("<td style='background-color:#cccccc;'>").Append(col.ColumnName).Append("</td>");
            }
            sb.Append("</tr>");
            // data rows
            foreach (DataRow row in dt.Rows)
            {
                sb.Append("<tr>");
                foreach (DataColumn col in dt.Columns)
                {
                    var val = row[col];
                    var sval = val == DBNull.Value ? "" : System.Net.WebUtility.HtmlEncode(val.ToString());
                    sb.Append("<td style='background-color:#eeeeee;'>").Append(sval).Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</table>");
            return sb.ToString();
        }

        public string GetTableDataAsHTML(string readdata)
        {
            var tablesin = readdata.Split('/');
//            var newQuery = sout[sout.Length - 1];
            string output = "<html><head>";
            output += "<script src='/jscode.js' type='text/javascript' charset='utf-8'></script>";
            output += "</head><body>";
            foreach (var eachtable in tablesin)
            {
                var newQuery = "Select * from " + eachtable;
                output += GetHTMLfromQuery(newQuery);
            }
            output += "</body></html>";
            return output;
        }

        ///////////////////////////////////////////////////////////////////////////
        // Datagrid editor API methods
        ///////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Get list of all tables in the database
        /// </summary>
        public List<string> GetTableList()
        {
            var tableNames = new List<string>();
            string strSQL = $"SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = '{DatabaseName}' ORDER BY TABLE_NAME;";
            
            var dt = GetQueryResults(strSQL);
            foreach (DataRow row in dt.Rows)
            {
                var name = row[0]?.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    tableNames.Add(name);
                }
            }
            
            return tableNames;
        }

        /// <summary>
        /// Get table data as JSON with columns and rows
        /// </summary>
        public string GetTableDataAsJson(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("Table name cannot be empty", nameof(tableName));

            // Validate table name to prevent SQL injection
            if (!IsValidTableName(tableName))
                throw new ArgumentException($"Invalid table name: {tableName}", nameof(tableName));

            var dt = GetQueryResults($"SELECT * FROM `{tableName}` LIMIT 1000;");
            var rows = new List<Dictionary<string, object>>();
            var columns = new List<string>();

            // Get column names
            foreach (DataColumn col in dt.Columns)
            {
                columns.Add(col.ColumnName);
            }

            // Convert rows to dictionaries
            foreach (DataRow row in dt.Rows)
            {
                var rowDict = new Dictionary<string, object>();
                foreach (DataColumn col in dt.Columns)
                {
                    rowDict[col.ColumnName] = row[col] ?? DBNull.Value;
                }
                rows.Add(rowDict);
            }

            var result = new { columns, rows };
            return JsonConvert.SerializeObject(result);
        }

        /// <summary>
        /// Save table data from JSON format
        /// </summary>
        public object SaveTableData(string tableName, string jsonData)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("Table name cannot be empty", nameof(tableName));

            if (!IsValidTableName(tableName))
                throw new ArgumentException($"Invalid table name: {tableName}", nameof(tableName));

            try
            {
                var data = JsonConvert.DeserializeObject<dynamic>(jsonData);
                
                if (data == null)
                    throw new ArgumentException("Invalid JSON format.");

                var rows = data["rows"];
                if (rows == null)
                    throw new ArgumentException("Invalid JSON format. Expected 'rows' array.");

                // For this simple implementation, we'll just return a success message
                // A full implementation would track changes, perform updates, inserts, deletes
                var rowCount = ((Newtonsoft.Json.Linq.JArray)rows).Count;
                // find table key
                // for each row
                // find the record with the key
                // if none, add record
                // else
                // update values listed
                
                return new 
                { 
                    success = true, 
                    message = $"Data received for table '{tableName}' with {rowCount} rows. (Full save not yet implemented)", 
                    rowCount 
                };
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid JSON format: " + ex.Message, ex);
            }
        }
    }
}
