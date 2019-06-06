module StockAnalyzer

open System
open System.IO
open System.Net
open System.Diagnostics
open System.IO
open FunctionalConcurrency

type StockData = {date:DateTime;open':float;high:float;low:float;close:float}

let Stocks = ["MSFT";"FB";"AAPL";"AMZN"; "GOOG"]

let displayStockInfo (symbolInfo:(string * StockData[])seq) elapsedTime =  
    let symbolInfo = symbolInfo |> Seq.map(fun (n,sinfo) ->  
                let stockData = 
                    sinfo 
                    |> Seq.map(fun s -> 
                        StockAnalyzer.StockData(s.date,s.open',s.high,s.low,s.close))
                    |> Seq.toArray
                (n, stockData))
    StockAnalyzer.StockUtils.DisplayStockInfo(symbolInfo,elapsedTime)


let convertStockHistory (stockHistory:string) = async {
    let stockHistoryRows =
        stockHistory.Split(
            Environment.NewLine.ToCharArray(),
            StringSplitOptions.RemoveEmptyEntries)
    return
        stockHistoryRows
        |> Seq.skip 1
        |> Seq.map(fun row -> row.Split(','))
        // this is a guard against bad CSV row formatting when for example the stock index
        |> Seq.filter(fun cells -> cells |> Array.forall(fun c -> not <| (String.IsNullOrWhiteSpace(c) || (c.Length = 1 && c.[0] = '-'))))
        |> Seq.map(fun cells ->
            {
                date = DateTime.Parse(cells.[0]).Date
                open' = float(cells.[1])
                high = float(cells.[2])
                low = float(cells.[3])
                close = float(cells.[4])
            })
        |> Seq.toArray
}

let googleSourceUrl symbol =
    sprintf "https://www.alphavantage.co/query?function=TIME_SERIES_DAILY_ADJUSTED&symbol=%s&outputsize=full&apikey=XC1HAECLPHJP7TO2&datatype=csv" symbol

let yahooSourceUrl symbol =
    sprintf "https://stooq.com/q/d/l/?s=%s.US&i=d" symbol

let downloadStockHistory' symbol = async {
    let url = googleSourceUrl symbol
    let req = WebRequest.Create(url)
    let! resp = req.AsyncGetResponse()
    use reader = new StreamReader(resp.GetResponseStream())
    return! reader.ReadToEndAsync()
}

let downloadStockHistory (symbol : string) = async {
    let file = sprintf "/Users/riccardo/Github/concombinators/src/Combinators/Data/Tickers/%s.csv" (symbol.ToLower())
    use stream = new StreamReader(file)
    return! stream.ReadToEndAsync() |> Async.AwaitTask
}

let processStockHistory symbol = async {
    let! stockHistory = downloadStockHistory symbol
    printfn "%s" stockHistory
    let! stockData = convertStockHistory stockHistory
    printfn "%A" stockData
    return (symbol, stockData)
}

     
let analyzeStockHistory() =
    let time = Stopwatch.StartNew()
    let stockInfo =
        Stocks
        |> Seq.map (processStockHistory)
        |> Async.Parallel
        |> Async.RunSynchronously
    displayStockInfo stockInfo time.ElapsedMilliseconds
    
