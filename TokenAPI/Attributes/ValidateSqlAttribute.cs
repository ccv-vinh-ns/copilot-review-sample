using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using TokenAPI.Common;

namespace TokenAPI.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ValidateSqlAttribute : Attribute, IAsyncActionFilter
    {
        /// <summary>
        /// Called before the action method executes. Extracts the SQL statement from the request body and validates its safety.
        /// If the SQL is unsafe or missing, sets the result to BadRequest and prevents the action from executing.
        /// </summary>
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Find the "Sql" property in the request body
            string? sql = null;
            foreach (var arg in context.ActionArguments.Values)
            {
                if (arg == null) continue;
                var propSql = arg.GetType().GetProperty("Sql");
                if (propSql != null)
                {
                    sql = propSql.GetValue(arg) as string;
                }
            }

            // Detect SQL type from route URL
            var path = context.HttpContext.Request.Path.Value?.ToLowerInvariant() ?? "";
            SqlType sqlType = SqlType.Select;
            if (path.Contains("insert")) sqlType = SqlType.Insert;
            else if (path.Contains("update")) sqlType = SqlType.Update;
            else if (path.Contains("delete")) sqlType = SqlType.Delete;
            else if (path.Contains("select")) sqlType = SqlType.Select;

            if (string.IsNullOrWhiteSpace(sql) || !IsSqlSafe(sql, sqlType))
            {
                context.Result = new BadRequestObjectResult(new { error = "Invalid or unsafe SQL statement." });
                return;
            }
            await next();
        }

        /// <summary>
        /// Validates whether the provided SQL statement is safe to execute.
        /// Only allows simple SELECT statements and blocks queries containing dangerous keywords (CREATE, DROP, ALTER, TRUNCATE, EXEC, MERGE),
        /// statement separators (;), and comments (--, /*, */).
        /// Returns true if the SQL is considered safe; otherwise, returns false.
        /// </summary>
        private bool IsSqlSafe(string sql, SqlType sqlType)
        {
            if (string.IsNullOrWhiteSpace(sql)) return false;
            var lowerSql = sql.Trim().ToLowerInvariant();

            // Block dangerous keywords and patterns for all types
            string[] dangerousKeywords = { "create", "drop", "alter", "truncate", "exec", "merge", ";", "--", "/*", "*/" };
            foreach (var keyword in dangerousKeywords)
            {
                if (lowerSql.Contains(keyword)) return false;
            }

            switch (sqlType)
            {
                case SqlType.Select:
                    // Must start with SELECT
                    if (!lowerSql.StartsWith("select")) return false;
                    break;
                case SqlType.Insert:
                    if (!lowerSql.StartsWith("insert")) return false;
                    break;
                case SqlType.Update:
                    if (!lowerSql.StartsWith("update")) return false;
                    break;
                case SqlType.Delete:
                    if (!lowerSql.StartsWith("delete")) return false;
                    break;
            }
            return true;
        }
    }
}
