module Channels.AsyncChannel


//Some usefull Async module extensions: 
//      1. CreateAsyncChannel - return channel for transmitting data between 
//         subsystems. Result is two functions - data sender and data receiver.
//      2. ParallelGrouped - async for launching sequence of asyncs in groups 
//         by specified count.
//      3. ParallelGroupedWithResults - async for launching sequence of asyncs 
//         in groups by specified count and return results as array.
// 2. can be usefull instead of (Async.Parallel >> Async.Ignore >> Async.Start) 
// for preventing threadpool starvation (especially on system startup)

module Async =
    type private msg<'a> = 
        | Result of 'a 
        | IntermediateResult of 'a 
        | GetResult of AsyncReplyChannel<'a*bool>
    let CreateAsyncChannel<'a> () =
        let inbox = new MailboxProcessor<_>( fun inbox ->
            let rec waitResponse (repl:AsyncReplyChannel<'a*bool>) = 
                inbox.Scan <| function
                    | GetResult repl -> 
                        Some <| waitResponse repl
                    | IntermediateResult res ->
                        repl.Reply (res, false)
                        Some <| waitRepl ()
                    | Result res -> 
                        repl.Reply (res, true)
                        Some <| async.Return ()
            and waitRepl () =
                inbox.Scan <| function
                    | GetResult repl -> Some <| waitResponse repl
                    | _ -> None
            waitRepl ()
        )
        inbox.Start()
        let resultWaiter timeout =             
            inbox.PostAndTryAsyncReply ((fun replChannel -> GetResult replChannel), timeout)
        let postResult closeChannel = 
            if closeChannel then Result else IntermediateResult
            >> inbox.Post
        (resultWaiter, postResult)
    let ParallelGrouped (delay:int) (n:int) (asyncs: seq<Async<_>>) = async {
        let delay = max 100 delay
        let asyncAcc, lastGroup =
            asyncs |> Seq.fold(fun (asyncAcc, group) op -> 
                let group = op::group
                if group.Length >= n then
                    async {
                        do! group |> Async.Parallel |> Async.Ignore                        
                        do! Async.Sleep delay
                        do! asyncAcc
                    }, []
                else asyncAcc, group) (async.Return (), [])
        do! asyncAcc
        do! lastGroup |> Async.Parallel |> Async.Ignore 
    }
    let ParallelGroupedWithResults (delay:int) (n:int) (asyncs: seq<Async<_>>) = async {
        let delay = max 100 delay
        let asyncAcc, lastGroup =
            asyncs |> Seq.fold(fun (asyncAcc, group) op -> 
                let group = op::group
                if group.Length >= n then
                    async {
                        let! res1 = group |> Async.Parallel                      
                        do! Async.Sleep delay
                        let! res2 = asyncAcc
                        return [|yield! res2; yield! res1|]
                    }, []
                else asyncAcc, group) (async.Return Array.empty, [])
        let! res1 = asyncAcc
        let! res2 = lastGroup |> Async.Parallel
        return [|yield! res1; yield! res2|]
    }

//usage
//dataReciever and dataSender could be passed to different independed parts of program to make channel
let (dataReciever, dataSender) = Async.CreateAsyncChannel<int>()

let timeout = 5000
let rec printer() = async {
    let! resultOpt = dataReciever timeout
    return!
        match resultOpt with
        | None -> //timeout
            printfn "So much free time - maybe other side has some problems"
            printer() //but continue 
        | Some (result, false) -> //intermediate result - channel still open 
            printfn "Retrieved message: %A" result
            printer()
        | Some (result, _) -> //channel is closed by this message
            printfn "Last message: %A" result
            async.Return ()
    }

let cancelation = new System.Threading.CancellationTokenSource()
Async.Start(printer(), cancelation.Token)

async {
    dataSender false 1
    do! Async.Sleep 1000
    dataSender false 2
    do! Async.Sleep 6000
    dataSender false 3
    do! Async.Sleep 2000
    dataSender true 4
} |> Async.Start

cancelation.Cancel()

//example of ParallelGrouped
seq {
    for i in 1..100 do yield async { printfn "%d" i }
} |> Async.ParallelGrouped 10000 10 |> Async.Start