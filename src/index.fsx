#r "../node_modules/fable-core/Fable.Core.dll"
#r "../node_modules/fable-powerpack/Fable.PowerPack.dll"
#load "../node_modules/fable-import-pixi/Fable.Import.Pixi.fs"

open System
open System.Collections.Generic
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.PIXI
open Fable.Import.PIXI.extras
open Fable.Import
open Fable.PowerPack

// Types -------------------------------------------

type Easing = Func<float, float, float, float, float>

// For performance, we use delegates instead of curried F# functions
type Behavior = Func<ESprite, float, Promise<bool>>

and ESprite(t:Texture, id: string, behaviors: Behavior list) =
    inherit Sprite(t)
    let mutable _behaviors = behaviors
    let mutable _disposed = false
    let mutable _prevTime = 0.

    member __.Id = id
    member __.IsDisposed = _disposed

    member self.Behave(b:Behavior) =
        _behaviors <- b :: _behaviors

    // Use a promise computation expression to iterate
    // through the behaviors as if they were synchronous
    member self.Update(dt: float) = promise {
        let behaviors = _behaviors
        _behaviors <- []
        let mutable notCompletedBehaviors = []
        let dt =
            let tmp = _prevTime
            _prevTime <- dt
            if tmp = 0. then 0. else dt - tmp
        for b in behaviors do
            let! complete = b.Invoke(self, dt)
            if not complete then
                notCompletedBehaviors <- b::notCompletedBehaviors
        // Some behaviors may have been added in the meanwhile with Behave
        _behaviors <- _behaviors @ notCompletedBehaviors
    }

    // System.IDisposable is the usual interface for disposable objects
    // in C#/F#. It lets you use language constructs like `use`. 
    interface IDisposable with
        member self.Dispose() =
            if not _disposed then
                _disposed <- true
                self.parent.removeChild(self) |> ignore

// Behaviors -------------------------------------------

module Behaviors =
    let private distanceBetween2Points (p1:Point) (p2:Point) =
        let tx = p2.x - p1.x
        let ty = p2.y - p1.y
        JS.Math.sqrt( tx * tx + ty * ty)

    let easeLinear = Easing(fun t b c d -> c * t / d + b)

    let accelerate(acc: Point) = Behavior(fun s _ ->
        s.position <- Point(s.position.x * acc.x, s.position.y + acc.y)
        Promise.lift true)

    // Use just functions instead of objects to represent behaviors.
    // As functions are closures, they can also keep state.
    let fade(easeFunction: Easing, duration) =
        let mutable ms = 0.
        Behavior(fun s dt ->
            if s.alpha > 0.
            then
                ms <- ms + dt
                let result = easeFunction.Invoke(ms, 0., 1., duration)
                s.alpha <- 1. - result
                if s.alpha < 0.
                then s.alpha <- 0.; true
                else false
            else true
            |> Promise.lift)

    let alphaDeath(onCompleted) = Behavior(fun s _ ->
        if s.alpha <= 0.
        then (s :> IDisposable).Dispose(); onCompleted s; true
        else false
        |> Promise.lift)

    let blink(freq) =
        let mutable ms = 0.
        Behavior(fun s dt ->
            ms <- ms + dt
            if ms > freq then
                s.visible <- not s.visible 
                ms <- 0.
            Promise.lift false) // blink never stops

    let move(speed: Point) = Behavior(fun s _ ->
        s.position <- Point(s.position.x + speed.x, s.position.y + speed.y)
        Promise.lift false) // moves never stops

    let moveTowardsFixedSmooth(p:Point, speed, radius) = Behavior(fun s _ ->
        let dist = distanceBetween2Points s.position p
        if dist > radius // approximate, should use some radius instead
        then
            let sp = s.position
            s.position <- Point(sp.x + (p.x - sp.x) / speed, sp.y + (p.y - sp.y) / speed)
            false
        else true
        |> Promise.lift)

    let moveTowardsMovingSmooth(target:Sprite, speed, radius) = Behavior(fun s _ ->
        let dist = distanceBetween2Points s.position target.position
        if dist > radius // approximate, should use some radius instead
        then
            let sp = s.position
            let tp = target.position
            s.position <- Point(sp.x + (tp.x - sp.x) / speed, sp.y + (tp.y - sp.y) / speed)
            false
        else true
        |> Promise.lift)

    let moveTowardsFixed(p:Point, speed, radius) = Behavior(fun s _ ->
        let sp = s.position
        let tx = p.x - sp.x
        let ty = p.y - sp.y
        let dist = JS.Math.sqrt( tx * tx + ty * ty)
        if dist > radius then // approximate, should use some radius instead
            let vx = (tx / dist) * speed
            let vy = (ty / dist) * speed
            s.position <- Point( sp.x + vx, sp.y + vy)
            false
        else true
        |> Promise.lift)

    let moveTowardsMoving(target:Sprite, speed, radius, onCompleted) = Behavior(fun s _ -> 
        let sp = s.position
        let tx = target.position.x - sp.x
        let ty = target.position.y - sp.y
        let dist = JS.Math.sqrt( tx * tx + ty * ty)
        if dist > radius // approximate, should use some radius instead
        then
            let vx = (tx / dist) * speed
            let vy = (ty / dist) * speed
            s.position <- Point( sp.x + vx, sp.y + vy)
            false
        else
            onCompleted s
            true
        |> Promise.lift)

    let killOffScreen(bounds: Rectangle, onCompleted) = Behavior(fun s _ ->
        let sx = s.position.x
        let sy = s.position.y
        let offScreen =
            (sx + s.width) < bounds.x
            || (sy + s.height) < bounds.y
            || (s.y - s.height) >= bounds.height
            || (sx - s.width) > bounds.width
        if offScreen then
            onCompleted s
            (s :> IDisposable).Dispose()
        Promise.lift offScreen)

// Animation ------------------------------------------------
open Behaviors

let rec animate (sprites: ESprite list) render dt =
    promise {
        let mutable xs = []
        for x in sprites do
            do! x.Update(dt)
            if not x.IsDisposed then xs <- x::xs
        return xs }
    |> Promise.iter(fun sprites ->
        render()
        Browser.window.requestAnimationFrame(fun dt ->
            animate sprites render dt) |> ignore)

let createSprite texture stageW stageH id =
    // Behaviors
    let dirX = if JS.Math.random() <= 0.5 then 1. else -1.
    let dirY = if JS.Math.random() <= 0.5 then 1. else -1.
    let b1 = move(Point(JS.Math.random() * 0.5 *  dirX, JS.Math.random() * 0.5 * dirY))
    let rect = Rectangle(0., 0., stageW, stageH)
    let b2 = killOffScreen(rect, fun s -> printfn "kof %s" s.Id)
    // Create Sprite
    let position = Point(JS.Math.random() * stageW, stageH * JS.Math.random())
    let dot = new ESprite(texture, id, [b1; b2], position=position)
    dot.anchor.x <- 0.5
    dot.anchor.y <- 0.5
    dot

// send one half of balls against the other half
let prepareHoming (sprite: ESprite, target: ESprite) =
    target.tint <- float 0x000000
    let death = alphaDeath(fun s -> printfn "alpha death %s" s.Id)
    moveTowardsMoving(target, 5., sprite.width, fun s ->
        // when dot reaches its goal, remove it and its target
        printfn "%s touched target" s.Id
        s.Behave(fade(easeLinear, 200.))
        s.Behave(death)
        target.Behave(fade(easeLinear, 400.))
        target.Behave(death))
    |> sprite.Behave

let start() =
    let renderer =
        WebGLRenderer(800., 600.,
            [ Antialias true
              BackgroundColor ( float 0x7A325D )])    
    Browser.document.getElementById("game").appendChild(renderer.view) |> ignore
    
    let stage = new Container(interactive=true)

    let texture =
        let g = Graphics()
        g.beginFill(float 0xFFFFFF) |> ignore
        g.drawCircle(0., 0., 10.) |> ignore
        g.endFill() |> ignore
        (g :> DisplayObject).generateTexture(
            U2.Case2 renderer, Globals.SCALE_MODES.LINEAR, 1.0)
            
    // Create our sprites
    let sprites =
        [0 .. 100] |> List.map
            (string >> createSprite texture renderer.width renderer.height)

    // Add sprites to stage and make some funny homing missiles
    sprites
    |> Seq.map (fun x -> stage.addChild(x) |> ignore; x)
    |> Seq.pairwise
    |> Seq.iteri (fun i tuple ->
        if i % 2 = 0 then prepareHoming tuple)

    // Show Time!
    animate sprites (fun () -> renderer.render(stage)) 0.

start()
