open System
open System.Linq
open MapReduce
open KMeans.Data
open KMeans.FsPSeq
open System.Threading.Tasks

[<STAThread>]
let main argv =

    
    // mapReduce : ('a -> 'b) -> ('b -> 'b -> 'b) -> 'a [] -> 'b
    let mapReduce f g xs =
        Array.map f xs  |> Array.reduce g
        
    let reduce f xs = mapReduce id f xs    
    let minBy f xs = mapReduce f min xs
    let maxBy f xs = mapReduce f max xs

    let mapReducePar f g xs =
        Array.Parallel.map f xs
        |> Array.reduce g
    
    
 
    let reduceParallel f (ie :'a array) =
      let rec reduceRec f (ie :'a array) = function
        | 1 -> ie.[0]
        | 2 -> f ie.[0] ie.[1]
        | len -> 
          let h = len / 2
          let o1 = Task.Run(fun _ -> reduceRec f (ie |> Array.take h) h)
          let o2 = Task.Run(fun _ -> reduceRec f (ie |> Array.skip h) (len-h))
          Task.WaitAll(o1, o2)
          f o1.Result o2.Result
      match ie.Length with
      | 0 -> failwith "Sequence contains no elements"
      | c -> reduceRec f ie c
    
    
    let mapReducePar f g xs =
        Array.Parallel.map f xs
        |> reduceParallel g
        
        
    let mapReducePar f g (i0: int) i1 =
          let results = System.Collections.Concurrent.ConcurrentBag()
          let init() = None
          let body i (state: ParallelLoopState) = function
            | None -> Some(f i)
            | Some x -> Some(g x (f i))
          let fin = function
            | None -> ()
            | Some x -> results.Add x
          let result = Parallel.For(i0, i1, init, body, fin)
          Seq.reduce g results
        

    let task f x =
        Task<_>.Factory.StartNew(fun () -> f x)
        
    let inline mapReduce f g i0 i2 =
        let rec loop d i0 i2 =
          let di = i2-i0
          if d=0 || di<2 then
            let mutable x = f i0
            for i=i0+1 to i2-1 do
              x <- g x (f i)
            x
          else
            let i1 = i0 + di/2
            let y = task (loop (d-1) i1) i2
            let x = loop (d-1) i0 i1
            g x y.Result
        if i0=i2 then invalidArg "i" "The input range was empty" else
        loop 8 i0 i2

    let initialCentroidsSet = data |> getRandomCentroids 11

    //  complete the Map-Reduce function in MapReduce.FsPSeq.fs file
    let run () = kmeans data dist initialCentroidsSet

    run ()
    
    Console.ReadLine() |> ignore
    0 // return an integer exit code