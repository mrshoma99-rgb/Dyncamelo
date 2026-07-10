using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes about the running Navisworks application.</summary>
[NodeCategory("Navisworks.Application")]
public static class ApplicationNodes
{
    /// <summary>The running Navisworks product name and API version.</summary>
    /// <returns>Product name (e.g. "Navisworks Manage 2024") and "major.minor" API version.</returns>
    [NodeName("Application.Version")]
    [NodeDescription("The running Navisworks product name and API version (report headers, compatibility checks).")]
    [NodeSearchTags("application", "version", "product", "api", "navisworks")]
    [MultiReturn("product", "apiVersion")]
    public static Dictionary<string, object?> Version()
    {
        try
        {
            var version = Autodesk.Navisworks.Api.Application.Version;
            return new Dictionary<string, object?>
            {
                ["product"] = version.RuntimeProductName,
                ["apiVersion"] = version.ApiMajor + "." + version.ApiMinor,
            };
        }
        catch (TypeInitializationException)
        {
            // Outside a Navisworks host the Application type cannot initialize.
            throw new InvalidOperationException(
                "The Navisworks application is not available. Run this node inside Navisworks.");
        }
    }
}
