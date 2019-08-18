module Program

open System
open System.IO

open System.Net.Mail
open System.Text

[<EntryPoint>]
let main argv =

    let create title body = 
        let client = new SmtpClient()
        client.Port <- 587
        client.Host <- "smtp.gmail.com";
        client.EnableSsl <- true
        client.Timeout <- 10000
        client.DeliveryMethod <- SmtpDeliveryMethod.Network
        client.UseDefaultCredentials <- false
        //client.Credentials <- new System.Net.NetworkCredential("tericcardo@gmail.com","gtmmegiopwzlxdyi") //"Jocker74!!");
       
        let mm = new MailMessage("tericcardo@gmail.com", "tericcardo@gmail.com", title, body)
        mm.BodyEncoding <- UTF8Encoding.UTF8
        mm.DeliveryNotificationOptions <- DeliveryNotificationOptions.OnFailure
        (client, mm)

    let emails =
        [0..1]
        |> List.map(fun _ -> async {
                    let (client, msg) = create "test" "test"
                    do! client.SendMailAsync(msg) |> Async.AwaitTask
                })
    emails|>Async.Parallel |> Async.Ignore |> Async.RunSynchronously  

    
    
    printfn "Enter stock symbol name: "
    let symbols = [ "APPL"; "AMZN"; "FB"; "GOOG" ] // Console.ReadLine()
    for symbol in symbols do     
        let x =
            StockAnalysis.doInvest symbol
            |> Async.RunSynchronously
        printfn "Recommendation for %s: %A" symbol x
    
    Console.ReadLine() |> ignore

    0
