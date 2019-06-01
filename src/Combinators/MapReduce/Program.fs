open System
open System.Linq
open MapReduce
open KMeans.Data
open KMeans.FsPSeq

[<EntryPoint>]
[<STAThread>]
let main argv =

    let initialCentroidsSet = data |> getRandomCentroids 11

    // TODO :
    //  complete the Map-Reduce function in MapReduce.FsPSeq.fs file
    let run () = kmeans data dist initialCentroidsSet

    run ()

    Console.ReadLine() |> ignore
    0 // return an integer exit code