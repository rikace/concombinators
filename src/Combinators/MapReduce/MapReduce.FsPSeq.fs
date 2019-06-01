namespace MapReduce

module MapReduceFsPSeq =

    open ParallelSeq
    open System.Linq

    //  Implementation of mapF function for the first phase of the MapReduce pattern
    let mapF  M (map:'in_value -> seq<'out_key * 'out_value>)
                (inputs:seq<'in_value>) =
        inputs
        |> PSeq.withExecutionMode ParallelExecutionMode.ForceParallelism
        |> PSeq.withDegreeOfParallelism M
        |> PSeq.collect (map)
        |> PSeq.groupBy (fst)
        |> PSeq.toList

    //  Implementation of reduceF function for the second phase of the MapReduce pattern
    let reduceF  R (reduce:'key -> seq<'value> -> 'reducedValues)
                   (inputs:('key * seq<'key * 'value>) seq) =
        inputs
        |> PSeq.withExecutionMode ParallelExecutionMode.ForceParallelism
        |> PSeq.withDegreeOfParallelism R
        |> PSeq.map (fun (key, items) ->
            items
            |> Seq.map (snd)
            |> reduce key)
        |> PSeq.toList

    //  Implementation of the MapReduce pattern composing the mapF and reduce functions
    let mapReduce
            (inputs:seq<'in_value>)
            (map:'in_value -> seq<'out_key * 'out_value>)
            (reduce:'out_key -> seq<'out_value> -> 'reducedValues)
            M R =

        inputs |> (mapF M map >> reduceF R reduce) //#A

