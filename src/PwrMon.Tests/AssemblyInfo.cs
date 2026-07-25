using Xunit;

// AppSettings.Current is a process-wide static shared by UnitFormatter tests; disabling
// parallelization keeps those tests from racing each other (or anything else) over it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
