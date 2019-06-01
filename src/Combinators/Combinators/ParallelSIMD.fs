module Combinators.ParallelSIMD

open  System
open  System.Threading
open  System.Threading.Tasks
open  System.Collections
open  System.Linq
open  System.Collections.Concurrent
open System.Collections.Immutable

//        var arr = Enumerable.Range(0, 1000).ToList();
//        var res = Reduce(arr, n => n, (a, b) => a + b);

let parallelReducer (data : 'a seq) (selector : 'a -> 'b) (reducer : 'b -> 'b -> 'b) (cts : CancellationTokenSource) =
    let partitioner = Partitioner.Create(data, EnumerablePartitionerOptions.NoBuffering)
    let results = ImmutableArray<'b>.Empty
    
    let opt = ParallelOptions(TaskScheduler = TaskScheduler.Default, CancellationToken = cts.Token,  MaxDegreeOfParallelism = Environment.ProcessorCount)

    Parallel.ForEach (
        partitioner,
        opt,
        (fun ()-> ResizeArray<'b>()),
        (fun item loopState (local : ResizeArray<'b>) ->
            local.Add(selector(item))
            local),        
        (fun (final) -> ImmutableInterlocked.InterlockedExchange(&results, results.AddRange(final)) |> ignore)) |> ignore
    
    results.AsParallel().Aggregate(reducer)
    
let parallelFilterMap (data : 'a array) (filter : 'a -> bool) (selector : 'a -> 'b) (reducer : 'b -> 'b -> 'b) (cts : CancellationTokenSource) =
    let partitioner = Partitioner.Create(0, data.Length)
    let results = ImmutableArray<'b>.Empty
    
    let opt = ParallelOptions(TaskScheduler = TaskScheduler.Default, CancellationToken = cts.Token,  MaxDegreeOfParallelism = Environment.ProcessorCount)

    Parallel.ForEach (
        partitioner,
        opt,
        (fun ()-> ResizeArray<'b>()),
        (fun (start, stop) loopState (local : ResizeArray<'b>) ->
            for j in [start..stop - 1] do
                if filter data.[j] then 
                    local.Add(selector(data.[j]))
            local),        
        (fun (final) -> ImmutableInterlocked.InterlockedExchange(&results, results.AddRange(final)) |> ignore)) |> ignore
    
    results.AsParallel().Aggregate(reducer)    

let executeInParallell (collection : 'a seq) (action : 'a -> Async<unit>) degreeOfParallelism =
    let queue = new ConcurrentQueue<'a>(collection)
    [0..degreeOfParallelism]
    |> Seq.map (fun t -> async {
        let mutable item = Unchecked.defaultof<'a>
        while queue.TryDequeue(&item) do
            do! action item    
    })
    |> Async.Parallel
            
let executeInParallelWithResult (collection : 'a seq) (action : 'a -> Async<'b>) degreeOfParallelism = async {
    let queue = new ConcurrentQueue<'a>(collection)
    let! data =
        [0..degreeOfParallelism]
        |> Seq.map (fun t -> async {
        let localResults = ResizeArray<'b>()
        let mutable item = Unchecked.defaultof<'a>
        while queue.TryDequeue(&item) do
            let! result = action item
            localResults.Add result
        return localResults
        })
        |> Async.Parallel
    return data |> Seq.concat |> Seq.toList
    }

let forEachAsync (source : 'a seq) dop (action : 'a -> Async<unit>) = 
    Partitioner.Create(source).GetPartitions(dop)
    |> Seq.map(fun partition -> async {
        use p = partition
        while p.MoveNext() do
            do! action p.Current
    })
    |> Async.Parallel
    
let forEachAsyncWithResult (source : 'a seq) dop (action : 'a -> Async<'b>) = async {
    let results = new ConcurrentBag<'b>()
    let! data =
        Partitioner.Create(source).GetPartitions(dop)
        |> Seq.map(fun partition -> async {
            let localResults = ResizeArray<'b>()
            use p = partition
            while p.MoveNext() do
                let! result = action p.Current
                localResults.Add result
            return localResults
        })
        |> Async.Parallel
    return data |> Seq.concat |> Seq.toList
    }