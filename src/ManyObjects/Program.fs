open System
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open FSharp.Data.Adaptive

module Shaders =

    open FShade
    type Vertex =
        {
            [<WorldPosition>]           wp : V4d
            [<Position>]                pos : V4d
            [<Color>]                   c  : V4d
            [<Semantic("Offset")>]      o  : V3d
            [<Semantic("InstanceColor")>] ic : V4d
        }

    let instanceOffset (v : Vertex) =
        vertex {
            return { v with pos = V4d(v.pos.XYZ, v.pos.W) + V4d(v.o.XYZ, 0.0); c = v.ic }
        }


[<EntryPoint;STAThread>]
let main argv = 
    Aardvark.Init()

    use app = new OpenGlApplication()
    use win = app.CreateGameWindow(1)

    let initialView = CameraView.lookAt (V3d(6,6,6)) V3d.Zero V3d.OOI


    // Use WASD to control the scene
    let view = initialView |> DefaultCameraController.control win.Mouse win.Keyboard win.Time
    let proj = win.Sizes |> AVal.map (fun s -> Frustum.perspective 60.0 0.1 100.0 (float s.X / float s.Y))

    let objectCount = 10000
    let rnd = new System.Random()

    // objects initially scattered around 0..scene size
    let sceneSize = 10.0

    // 3d vectors (float32s) for fast copy to gpu
    let currentPositions = 
        Array.init objectCount (fun s -> V3f(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()) * float32 sceneSize)

    // can be used to encode simulation properties
    let currentColor =
        Array.init objectCount (fun _ -> C4f(1.0, 1.0, 0.0, 1.0))

    let positions = 
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let mutable lastFrame = sw.Elapsed.TotalSeconds
        win.Time |> AVal.map (fun _ -> 
            // compute delta since last frame
            let dt = sw.Elapsed.TotalSeconds - lastFrame
            lastFrame <- sw.Elapsed.TotalSeconds

            // measure your performance only for this block

            for i in 0 .. currentPositions.Length - 1 do
                let old = currentPositions[i]
                let delta = V3f(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()) * (float32 (dt * 10.0))
                currentPositions[i] <- old + delta

            // return current state
            currentPositions, currentColor
        )

    
    
    let sg =
        let sphere = Primitives.unitSphere 5
        let pos  = sphere.IndexedAttributes.[DefaultSemantic.Positions]
        let norm = sphere.IndexedAttributes.[DefaultSemantic.Normals]
        let call = DrawCallInfo(FaceVertexCount = pos.Length, InstanceCount = objectCount)

        Sg.render IndexedGeometryMode.TriangleList call
        |> Sg.vertexBuffer DefaultSemantic.Positions (BufferView(AVal.constant (ArrayBuffer pos :> IBuffer), typeof<V3f>))
        |> Sg.vertexBuffer DefaultSemantic.Normals (BufferView(AVal.constant (ArrayBuffer norm :> IBuffer), typeof<V3f>))
        |> Sg.instanceAttribute "Offset"        (AVal.map (Array.copy << fst) positions)
        |> Sg.instanceAttribute "InstanceColor" (AVal.map (Array.copy << snd) positions)
        |> Sg.scale 0.01
        |> Sg.shader {
                do! Shaders.instanceOffset 
                do! DefaultSurfaces.trafo 
                do! DefaultSurfaces.simpleLighting
        }
        |> Sg.viewTrafo (view |> AVal.map CameraView.viewTrafo)
        |> Sg.projTrafo (proj |> AVal.map Frustum.projTrafo)

    
    let task =
        app.Runtime.CompileRender(win.FramebufferSignature, sg)

    win.RenderTask <- task
    win.Run()
    0
