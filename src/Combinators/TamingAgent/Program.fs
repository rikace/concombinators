module Program

open System.IO
open System
open System.Threading
open TamingAgentModule

open SixLabors.ImageSharp
open SixLabors.ImageSharp.PixelFormats
open Combinators.AsyncEx.AsyncCombinators


[<AutoOpen>]
module HelperType =
    type ImageInfo = { Path:string; Name:string; Image:Image<Rgba32>}

module ImageHelpers =
 

    let convertImageTo3D (image:Image<Rgba32> ) =
        Helpers.ImageHandler.ConvertTo3D(image)

    // The TamingAgent in action for image transformation
    let loadImage = (fun (imagePath:string) -> async {
        let bitmap = Image.Load(imagePath)
        printfn "Loading image %s - Thread #id: %d" (Path.GetFileNameWithoutExtension(imagePath)) Thread.CurrentThread.ManagedThreadId    
        return { Path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                 Name = Path.GetFileName(imagePath)
                 Image = bitmap } })

    let apply3D = (fun (imageInfo:ImageInfo) -> async {
        let bitmap = convertImageTo3D imageInfo.Image
        printfn "Apply 3D image %s - Thread #id: %d" (Path.GetFileNameWithoutExtension(imageInfo.Name)) Thread.CurrentThread.ManagedThreadId    
        return { imageInfo with Image = bitmap } })

    let saveImage = (fun (imageInfo:ImageInfo) -> async {        
        let destination = Path.Combine(imageInfo.Path, (sprintf "%s_modf.jpg" (Path.GetFileNameWithoutExtension(imageInfo.Name))))
        printfn "Saving image %s - Thread #id: %d" (Path.GetFileNameWithoutExtension(imageInfo.Name)) Thread.CurrentThread.ManagedThreadId    
        imageInfo.Image.Save(destination)
        return imageInfo.Name})


module ``TamingAgent example`` =

    open Combinators.AsyncEx.AsyncCombinators
    open ImageHelpers

    let run (action : 'a -> unit) (x : Async<'a>) =
        Async.StartWithContinuations(x, action, ignore, ignore)
            
    
    let loadandApply3dImage imagePath = Async.retn imagePath >>= loadImage >>= apply3D >>= saveImage

    let loadandApply3dImageAgent = TamingAgent<string, string>(2, loadandApply3dImage)

    let _ = loadandApply3dImageAgent.Subscribe(fun imageName -> printfn "Saved image %s - from subscriber" imageName)

    let transformImages() =
        let images = Directory.GetFiles(@"./Images")
        for image in images do
            loadandApply3dImageAgent.Ask(image) |> run (fun imageName -> printfn "Saved image %s - from reply back" imageName)


module ``Composing TamingAgent with Kleisli operator example`` =
    open Kleisli
    open ImageHelpers
        
    let degreePar = 3        
    
    let pipelineAgent (limit:int) (operation:'a -> Async<'b>) : ('a -> Async<'b>) =
        let agent = TamingAgent(limit, operation)
        // tamingAgent.PostAndAsyncReply(fun ch -> Ask(value, ch))
        fun (job: 'a) -> agent.Ask job
       
    
    // >=>
    let loadImageAgent = pipelineAgent degreePar loadImage
    let apply3DEffectAgent = pipelineAgent degreePar apply3D
    let saveImageAgent = pipelineAgent degreePar saveImage
    
    
    let pipelineKleisli = loadImageAgent >=> apply3DEffectAgent >=> saveImageAgent

    let transformImages() =
        let images = Directory.GetFiles(@"./Images")
        for image in images do
            pipelineKleisli image |> run (fun imageName -> printfn "Saved image %s" imageName)



[<EntryPoint>]
let main argv =
    
    
    ``Composing TamingAgent with Kleisli operator example``.transformImages()
    

    Console.ReadLine() |> ignore
    0