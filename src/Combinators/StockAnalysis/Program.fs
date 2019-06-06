module Program

open System
open System.IO

[<EntryPoint>]
let main argv =
    
    
    printfn "Enter stock symbol name: "
    let symbols = [ "APPL"; "AMZN"; "FB"; "GOOG" ] // Console.ReadLine()
    for symbol in symbols do     
        let x =
            StockAnalysis.doInvest symbol
            |> Async.RunSynchronously
        printfn "Recommendation for %s: %A" symbol x
    
    Console.ReadLine() |> ignore

    0
