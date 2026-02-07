using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Horizon.WebApi.Filters
{
    public class SetVersionInPathDocumentFilter : IDocumentFilter
    {
        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            var updatedPaths = new OpenApiPaths();

            foreach (var entry in swaggerDoc.Paths)
            {
                updatedPaths.Add(
                    entry.Key.Replace("{version}", swaggerDoc.Info.Version),
                    entry.Value);
            }

            swaggerDoc.Paths = updatedPaths;
        }
    }

    public class RemoveVersionParameterOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (operation.Parameters.Count > 0)
            {
                try
                {
                    var versionParameter = operation.Parameters.Single(p => p.Name == "version");

                    if (versionParameter != null)
                        operation.Parameters.Remove(versionParameter);
                }
                catch (Exception ex)
                {

                }
            }
        }
    }
}
