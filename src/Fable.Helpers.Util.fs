namespace Fable.Helpers.Util

type Promise<'T> = Fable.Import.JS.Promise<'T>

[<RequireQualifiedAccess>]
module Promise =
    open System
    open Fable.Core
    open Fable.Core.JsInterop
    
    let inline lift<'T> (a: 'T): Promise<'T> =
        Fable.Import.JS.Promise.resolve(U2.Case1 a)

    let inline bind (a: 'T->Promise<'R>) (pr: Promise<'T>): Promise<'R> =
        unbox(pr?``then``(a))

    let inline map (a: 'T->'R) (pr: Promise<'T>): Promise<'R> =
        unbox(pr?``then``(a))

    let inline iter (a: 'T->unit) (pr: Promise<'T>): unit =
        ignore(pr?``then``(a))

    let inline catch (a: obj->'T) (pr: Promise<'T>): Promise<'T> =
        unbox(pr?catch(a))
        
    let inline either (success: 'T->'R) (fail: obj->'R) (pr: Promise<'T>): Promise<'R> =
        unbox(pr?``then``(success, fail))

    type PromiseBuilder() =
        member x.Bind(p: Promise<'T>, f: 'T->Promise<'R>) = bind f p
        member x.Combine(p1: Promise<unit>, p2: Promise<'T>) = bind (fun () -> p2) p1
        member x.For(seq: seq<'T>, body: 'T->Promise<unit>) =
            (lift (), seq)
            ||> Seq.fold (fun p a ->
                bind (fun () -> body a) p)
        member x.While(guard, p): Promise<unit> =
            if guard()
            then bind (fun () -> x.While(guard, p)) p
            else lift ()
        member x.Return(a: 'T) = lift a
        member x.ReturnFrom(p: Promise<'T>) = p
        member x.Zero() = lift ()
        member x.TryFinally(p: Promise<'T>, compensation: unit->unit) =
            either (fun x -> compensation(); x) (fun ex -> compensation(); raise(unbox ex)) p
        member x.TryWith(p: Promise<'T>, catchHandler: obj->Promise<'T>) =
            unbox<Promise<'T>>(p?catch(catchHandler))
        member x.Delay(generator: unit->Promise<'T>): Promise<'T> =
            createObj ["then" ==> fun x -> generator()?``then``(x)] |> unbox
        member x.Using<'T, 'R when 'T :> IDisposable>(resource: 'T, binder: 'T->Promise<'R>) =
            x.TryFinally(binder(resource), fun () -> resource.Dispose())

[<AutoOpen>]
module PromiseImpl =
    let promise = Promise.PromiseBuilder()
