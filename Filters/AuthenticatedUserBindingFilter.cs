using System.Reflection;
using System.Security.Claims;
using System.Collections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JSAPNEW.Filters
{
    public class AuthenticatedUserBindingFilter : IActionFilter
    {
        private static readonly HashSet<string> UserIdentityNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "userId",
            "createdBy",
            "updatedBy"
        };

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
                return;

            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
                return;

            if (user.IsInRole("Admin") || user.IsInRole("Super User") || user.IsInRole("SuperAdmin"))
                return;

            var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("userId");
            if (!int.TryParse(userIdValue, out var authenticatedUserId) || authenticatedUserId <= 0)
                return;

            foreach (var argument in context.ActionArguments.ToList())
            {
                if (UserIdentityNames.Contains(argument.Key))
                {
                    context.ActionArguments[argument.Key] = authenticatedUserId;
                    continue;
                }

                if (argument.Value == null || argument.Value is string)
                    continue;

                SetIdentityProperties(argument.Value, authenticatedUserId, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }

        private static void SetIdentityProperties(object target, int authenticatedUserId, HashSet<object> visited, int depth)
        {
            if (target == null || depth > 8)
                return;

            var targetType = target.GetType();
            if (IsSimpleType(targetType))
                return;

            if (!visited.Add(target))
                return;

            if (target is IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                        SetIdentityProperties(item, authenticatedUserId, visited, depth + 1);
                }

                return;
            }

            var properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (property.CanWrite && UserIdentityNames.Contains(property.Name))
                {
                    if (property.PropertyType == typeof(int))
                        property.SetValue(target, authenticatedUserId);
                    else if (property.PropertyType == typeof(int?))
                        property.SetValue(target, authenticatedUserId);
                    else if (property.PropertyType == typeof(long))
                        property.SetValue(target, (long)authenticatedUserId);
                    else if (property.PropertyType == typeof(long?))
                        property.SetValue(target, (long)authenticatedUserId);
                    else if (property.PropertyType == typeof(string))
                        property.SetValue(target, authenticatedUserId.ToString());

                    continue;
                }

                if (!property.CanRead || IsSimpleType(property.PropertyType) || property.GetIndexParameters().Length > 0)
                    continue;

                var value = property.GetValue(target);
                if (value != null)
                    SetIdentityProperties(value, authenticatedUserId, visited, depth + 1);
            }
        }

        private static bool IsSimpleType(Type type)
        {
            var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
            return effectiveType.IsPrimitive ||
                   effectiveType.IsEnum ||
                   effectiveType == typeof(string) ||
                   effectiveType == typeof(decimal) ||
                   effectiveType == typeof(DateTime) ||
                   effectiveType == typeof(DateTimeOffset) ||
                   effectiveType == typeof(Guid);
        }
    }
}
