using Xunit;

// Each SQL fixture creates/drops its own database. Concurrent CREATE DATABASE calls
// contend for SQL Server's model database lock. Serialize fixtures, not the explicit
// Task.WhenAll writers/leases/requests that exercise concurrency inside each test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
