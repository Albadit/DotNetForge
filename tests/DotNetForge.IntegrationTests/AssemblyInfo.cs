using Xunit;

// Environment-variable-based database selection means the host must build one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
