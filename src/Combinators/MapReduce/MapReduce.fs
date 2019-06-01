module MapReduce.MapReduce

module MapReduceSequential =

    let mapReduce
            (inputs:seq<'in_value>)
            (map:'in_value -> seq<'out_key * 'out_value>)
            (reduce:'out_key -> seq<'out_value> -> 'reducedValues)
            M R =
        inputs
        |> Seq.collect (map)
        |> Seq.groupBy (fst)
        |> Seq.map (fun (key, items) ->
            items
            |> Seq.map (snd)
            |> reduce key)
        |> Seq.toList

module ParellelMapReduce = 

    open ParallelSeq
    open System.Linq
    open System.Collections.Generic

    //  Implementation of mapF function for the first phase of the MapReduce pattern
    let mapF  M (map:'in_value -> seq<'out_key * 'out_value>)
                (inputs:seq<'in_value>) =
        inputs
        |> PSeq.withExecutionMode ParallelExecutionMode.ForceParallelism  
        |> PSeq.withDegreeOfParallelism M  
        |> PSeq.collect (map)  
        |> PSeq.groupBy (fst) 
        |> PSeq.toList 

    // Implementation of reduceF function for the second phase of the MapReduce pattern
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

     
    let mapReduce
            (inputs:seq<'in_value>)
            (map:'in_value -> seq<'out_key * 'out_value>)
            (reduce:'out_key -> seq<'out_value> -> 'reducedValues)
            M R =

        inputs |> (mapF M map >> reduceF R reduce)  

    let map = fun (fileName:string, fileContent:string) ->
                let l = new List<string * int>()
                let wordDelims = [|' ';',';';';'.';':';'?';'!';'(';')';'\n';'\t';'\f';'\r';'\b'|]
                fileContent.Split(wordDelims) |> Seq.iter (fun word -> l.Add((word, 1)))
                l :> seq<string * int>

    let reduce = fun key (values:seq<int>) -> [values |> Seq.sum] |> seq<int>
    let partitionF = fun key M -> abs(key.GetHashCode()) % M 
    let testInput = ["File1", "I was going to the airport when I saw someone crossing";
                     "File2", "I was going home when I saw you coming toward me"] |> List.toSeq
                               
    let output = mapReduce testInput map reduce 2 2