module ImagePipeline

open SixLabors.ImageSharp
open SixLabors.ImageSharp.PixelFormats
open System.Threading
open System.IO
open HelpersFSharp
open Helpers

open System
//open System.Drawing
open SixLabors.ImageSharp
open SixLabors.ImageSharp.Formats.Jpeg
open SixLabors.ImageSharp.PixelFormats
open SixLabors.ImageSharp.Processing
open SixLabors.ImageSharp.Processing.Processors
open SixLabors.ImageSharp.Processing.Processors.Overlays
open SixLabors.Primitives
open System.Collections.Generic

[<AutoOpenAttribute>]
module AsyncQueue =

    // represent a queue operation
    type Instruction<'T> =
        | Enqueue of 'T * (unit -> unit) 
        | Dequeue of ('T -> unit)

    type AsyncBoundedQueue<'T> (capacity: int, ?cancellationToken:CancellationTokenSource) =
        let waitingConsumers, elts, waitingProducers = Queue(), Queue<'T>(), Queue()
        let cancellationToken = defaultArg cancellationToken (new CancellationTokenSource())

(*  The following balance function shuffles as many elements through the queue 
    as possible by dequeuing if there are elements queued and consumers waiting 
    for them and enqueuing if there is capacity spare and producers waiting *)
        let rec balance() =
            if elts.Count > 0 && waitingConsumers.Count > 0 then
                elts.Dequeue() |> waitingConsumers.Dequeue()
                balance()
            elif elts.Count < capacity && waitingProducers.Count > 0 then
                let x, reply = waitingProducers.Dequeue()
                reply()
                elts.Enqueue x
                balance()

(*  This agent sits in an infinite loop waiting to receive enqueue and dequeue instructions, 
    each of which are queued internally before the internal queues are rebalanced *)
        let agent = MailboxProcessor.Start((fun inbox ->
                let rec loop() = async { 
                        let! msg = inbox.Receive()
                        match msg with
                        | Enqueue(x, reply) -> waitingProducers.Enqueue (x, reply)
                        | Dequeue reply -> waitingConsumers.Enqueue reply
                        balance()
                        return! loop() }
                loop()), cancellationToken.Token)

        member __.AsyncEnqueue x =
              agent.PostAndAsyncReply (fun reply -> Enqueue(x, reply.Reply))
        member __.AsyncDequeue() =
              agent.PostAndAsyncReply (fun reply -> Dequeue reply.Reply)

        interface System.IDisposable with          
              member __.Dispose() = 
                cancellationToken.Cancel()
                (agent :> System.IDisposable).Dispose()
    
type ImageFilters =
    | Red = 0
    | Green = 1
    | Blue = 2
    | Gray = 3
    
module ImageHandlers = 
    let configuration = new Configuration(new JpegConfigurationModule())
    
    let resize newWidth newHeight (source : Image<Rgba32>) = 
        let image = source.Clone()
        image.Mutate(new Action<_>(fun (x : IImageProcessingContext<Rgba32>) -> x.Resize(400, 400) |> ignore))
        image
        
    let convertTo3D (source : Image<Rgba32>) = 
        let image = source.Clone()
        let w = image.Width
        let h = image.Height
        for x = 20 to w - 1 do
            for y = 0 to h - 1 do
                let  c1 = image.[x, y]               
                let c2 = image.[x - 20, y]
                image.[x - 20, y] <- Rgba32(c1.R, c2.G, c2.B)
           
        image


    let setFilter (imageFilter : ImageFilters) (source : Image<Rgba32>) =
        let image = source.Clone()
        let w = image.Width
        let h = image.Height
        for x = 0 to w - 1 do
            for y = 0 to h - 1 do
                let p = image.[x, y]                
                if imageFilter = ImageFilters.Red then
                    image.[x, y] <- Rgba32(image.[x, y].R, 0uy, 0uy)
                elif imageFilter = ImageFilters.Green then
                    image.[x, y] <- Rgba32(0uy, image.[x, y].G, 0uy)
                elif imageFilter = ImageFilters.Blue then
                    image.[x, y] <- Rgba32(0uy, 0uy, image.[x, y].B)
                elif imageFilter = ImageFilters.Gray then
                    let gray = (byte)((0.299 * (double)p.R) + (0.587 * (double)p.G) + (0.114 * (double)p.B))
                    image.[x, y] <- Rgba32(gray, gray, gray)
                    
                image.[x, y] <- p
        image
        
    let load (imageSrc:string) = Image.Load(imageSrc)    
    let saveImage (destination:string) (image:Image<Rgba32>) =
        use stream = File.Create(destination)
        image.SaveAsJpeg(stream)
        
    module Interpolation =
            
        let lerp (s:float32) (e:float32) (t:float32) =
            s + (e - s) * t
         
        let blerp c00 c10 c01 c11 tx ty =
            lerp (lerp c00 c10 tx) (lerp c01 c11 tx) ty
         
        let scale (self:Image<Rgba32>) (scaleX:float) (scaleY:float) =
            let newWidth  = int ((float self.Width)  * scaleX)
            let newHeight = int ((float self.Height) * scaleY)    
            let newImage = new Image<Rgba32>(newWidth, newHeight)
            for x in 0..newWidth-1 do
                for y in 0..newHeight-1 do
                    let gx = (float32 x) / (float32 newWidth) *  (float32 (self.Width  - 1))
                    let gy = (float32 y) / (float32 newHeight) * (float32 (self.Height - 1))
                    let gxi = int gx
                    let gyi = int gy
                    let c00 = self.[gxi, gyi]
                    let c10 = self.[gxi + 1, gyi]
                    let c01 = self.[gxi,     gyi + 1]
                    let c11 = self.[gxi + 1, gyi + 1]
                    let red   = (blerp (float32 c00.R) (float32 c10.R) (float32 c01.R) (float32 c11.R) (gx - (float32 gxi)) (gy - (float32 gyi)))
                    let green = (blerp (float32 c00.G) (float32 c10.G) (float32 c01.G) (float32 c11.G) (gx - (float32 gxi)) (gy - (float32 gyi)))
                    let blue  = (blerp (float32 c00.B) (float32 c10.B) (float32 c01.B) (float32 c11.B) (gx - (float32 gxi)) (gy - (float32 gyi)))
                    newImage.[x, y] <- Rgba32(red, green,  blue)
            newImage
         
         
type ImageInfo = { Name:string; Destination:string; mutable Image:Image<Rgba32>}

let capacityBoundedQueue = 10
let cts = new CancellationTokenSource()

let loadedImages = new AsyncBoundedQueue<ImageInfo>(capacityBoundedQueue, cts)
let scaledImages = new AsyncBoundedQueue<ImageInfo>(capacityBoundedQueue, cts)    
let filteredImages = new AsyncBoundedQueue<ImageInfo>(capacityBoundedQueue, cts)    


// string -> string -> ImageInfo
let loadImage imageName destination = async {
    printfn "%s" (imageName)
    let mutable image = Image.Load(imageName)    
    return { Name = imageName; 
             Destination = Path.Combine(destination, Path.GetFileName(imageName)); 
             Image=image }
    }

// ImageInfo -> ImageInfo  
let scaleImage (info:ImageInfo) = async {
    let scale = 200    
    let image = info.Image.Clone()
    let image' = info.Image   
    info.Image.Dispose()
    info.Image <- null 
    let mutable resizedImage = ImageHandlers.resize scale scale image 

    return { info with Image = resizedImage } }

let filterRed (info:ImageInfo) filter = async {
    let image = info.Image.Clone()   
    let mutable filterImage = ImageHandlers.setFilter filter image
    return { info with Image = filterImage } }
    
let loadImages = async {
    let images = Directory.GetFiles("../../Data/paintings")
    for image in images do 
        printfn "Loading %s" image 
        let! info = loadImage image "../../Data/Images"
        do! loadedImages.AsyncEnqueue(info) }
    
let scaleImages = async {
    while not cts.IsCancellationRequested do 
        let! info = loadedImages.AsyncDequeue()
        let! info = scaleImage info
        do! scaledImages.AsyncEnqueue(info) }

let filters = [| ImageFilters.Blue; ImageFilters.Green;
                 ImageFilters.Red; ImageFilters.Gray |]

let filterImages = async {
    while not cts.IsCancellationRequested do         
        let! info = scaledImages.AsyncDequeue()
        for filter in filters do
            let! imageFiltered = filterRed info filter
            do! filteredImages.AsyncEnqueue(imageFiltered) }
 
let saveImages = async {
    while not cts.IsCancellationRequested do
        let! info = filteredImages.AsyncDequeue()      
        printfn "saving %s"  info.Name 
        info.Image.Save(info.Destination)
        info.Image.Dispose()
     }
 
let start() =
    Async.Start(loadImages, cts.Token)
    Async.Start(scaleImages, cts.Token)
    Async.Start(filterImages, cts.Token)
    Async.Start(saveImages, cts.Token)


                