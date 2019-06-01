module Program

open System.IO
open System
open System.Threading
open TamingAgentModule
open SixLabors.ImageSharp
open SixLabors.ImageSharp.PixelFormats

[<AutoOpen>]
module HelperType =
    type ImageInfo = { Path:string; Name:string; Image:Image<Rgba32>}

module ImageHelpers =
 

    let convertImageTo3D (image:Image<Rgba32> ) =
        Helpers.ImageHandler.ConvertTo3D(image)

    // The TamingAgent in action for image transformation
    let loadImage = (fun (imagePath:string) -> async {
        let bitmap = Image.Load(imagePath)
        return { Path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                 Name = Path.GetFileName(imagePath)
                 Image = bitmap } })

    let apply3D = (fun (imageInfo:ImageInfo) -> async {
        let bitmap = convertImageTo3D imageInfo.Image
        return { imageInfo with Image = bitmap } })

    let saveImage = (fun (imageInfo:ImageInfo) -> async {
        printfn "Saving image %s" imageInfo.Name
        let destination = Path.Combine(imageInfo.Path, imageInfo.Name)
        imageInfo.Image.Save(destination)
        return imageInfo.Name})


module ``TamingAgent example`` =

    open AsyncEx
    open ImageHelpers

    let loadandApply3dImage imagePath = Async.retn imagePath >>= loadImage >>= apply3D >>= saveImage

    let loadandApply3dImageAgent = TamingAgent<string, string>(2, loadandApply3dImage)

    let _ = loadandApply3dImageAgent.Subscribe(fun imageName -> printfn "Saved image %s - from subscriber" imageName)

    let transformImages() =
        let images = Directory.GetFiles(@".\Images")
        for image in images do
            loadandApply3dImageAgent.Ask(image) |> run (fun imageName -> printfn "Saved image %s - from reply back" imageName)


module ``Composing TamingAgent with Kleisli operator example`` =
    open Kleisli
    open AsyncEx
    open ImageHelpers
    // The TamingAgent with Kleisli operator

    
    let pipelineAgent (limit:int) (operation:'a -> Async<'b>) (job:'a) : Async<_> =
        let agent = TamingAgent(limit, operation)
        agent.Ask(job)

    // (‘a -> Async<’b>) -> Async<’a> -> Async<’b>
    let agentBind f xAsync = async {
        let! x = xAsync
        return! f x }
   
    let agent1 = pipelineAgent 1 (fun x -> Async.retn( sprintf "Pipeline 1 processing message : %s" x ))
    let agent2 = pipelineAgent 1 (fun x -> Async.retn( sprintf "Pipeline 2 processing message : %s" x ))

    let message i = sprintf "Message %d sent to the pipeline" i

    for i in [0..5] do
        agent1 (string i) |> Async.run (fun res -> printfn "%s" res)


    let pipelineBind x = Async.retn x >>= agent1 >>= agent2

    for i in [1..10] do
        pipelineBind (string i)
        |> Async.run (fun res -> printfn "Thread #id: %d - Msg: %s" Thread.CurrentThread.ManagedThreadId res)    
    
    
    
    // >=>
    let loadImageAgent = pipelineAgent 2 loadImage
    let apply3DEffectAgent = pipelineAgent 2 apply3D
    let saveImageAgent = pipelineAgent 2 saveImage
    
    let pipelineKleisli = loadImageAgent >=> apply3DEffectAgent >=> saveImageAgent

    let transformImages() =
        let images = Directory.GetFiles(@".\Images")
        for image in images do
            pipelineKleisli image |> run (fun imageName -> printfn "Saved image %s" imageName)



[<EntryPoint>]
let main argv =

    ``TamingAgent example``.transformImages()

    ``Composing TamingAgent with Kleisli operator example``.transformImages();

    Console.ReadLine() |> ignore
    0