using Xunit;

// The tests share one WPF dispatcher thread and a few process-wide statics; running them one after another
// costs seconds and removes a class of nondeterministic hangs.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
