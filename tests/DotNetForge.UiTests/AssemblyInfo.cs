using Xunit;

// Each host's database name goes through a process-wide environment variable, so two hosts must not start at once.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
