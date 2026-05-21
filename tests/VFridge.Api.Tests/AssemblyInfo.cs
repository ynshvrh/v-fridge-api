using Xunit;

// Tests share process-level environment variables (DotNetEnv + Program.cs reads JwtOptions
// and ConnectionStrings:Default before WebApplicationFactory.ConfigureAppConfiguration has a
// chance to apply, so the test factory has to seed env vars). That makes class-level
// parallelism unsafe — one class's connection string would clobber another's mid-flight.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
