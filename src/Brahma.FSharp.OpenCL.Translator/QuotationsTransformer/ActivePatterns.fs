module Brahma.FSharp.OpenCL.QuotationsTransformer.ActivePatterns

open FSharp.Quotations
open FSharp.Quotations.Patterns
open FSharp.Reflection

open Brahma.FSharp.OpenCL.QuotationsTransformer.Common

let rec (|HasSubExpr|_|) ((|Pattern|_|) : Expr -> 'a Option) expr =
    match expr with
    | Pattern x -> Some x
    | ExprShape.ShapeCombination (shapeObj, exprList) ->
        exprList
        |> List.map  ((|HasSubExpr|_|) (|Pattern|_|))
        |> List.fold (fun x y ->
            match x with
            | Some _ -> x
            | None -> y) None
    | _ -> None

/// An active pattern to recognize any value expression
/// which is an arbitrary depth subterm of the expression
let (|HasValueAsSubExpr|_|) (expr: Expr) = (|HasSubExpr|_|) (|Value|_|) expr

/// An active pattern to recognize lambda expression,
/// that obtained from printf/printfn function.
/// Example: printf "%d %f" -> ([Int, Float], "%d %f")
let (|NewPrintfFormat|_|) (expr: Expr) =
    match expr with
    | Call (None, mInfo, args) ->
        match mInfo.Name with
        | "PrintFormat" | "printfn" ->
            let retType = mInfo.ReturnType
            let bindTypes =
                match retType with
                | _ when retType = typeof<unit> -> []
                | _ when FSharpType.IsFunction retType ->
                    getFunctionArgTypes <| mInfo.ReturnType
                | _ -> failwithf "printf: returned type %A of NewPrintfFormat is not expected" retType

            match args with
            | [HasValueAsSubExpr (s, _)] ->
                let s' = (s :?> string).Replace("\n", "\\n")
                let s'' = if mInfo.Name = "printfn" then s' + "\\n" else s'
                Some (bindTypes, s'')
            | _ -> failwithf "printf: argument %A of NewPrintfFormat call is not expected" args
        | _ -> None
    | _ -> None

let rec (|PartialPrintf|_|) (expr: Expr) =
    match expr with
    | Let(_, value, inExpr) ->
        match value with
        | NewPrintfFormat (tpArgs, value) ->
            assert (tpArgs = getFunctionArgTypes inExpr.Type)
            Some (tpArgs, value, [])
        | _ -> None
    | Application(f, arg) ->
        match f with
        | PartialPrintf(tpArgs, value, bindArgs) ->
            Some (tpArgs, value, bindArgs @ [arg])
        | _ -> None
    | NewPrintfFormat(tpArgs, formatStr) ->
        Some (tpArgs, formatStr, [])
    | _ -> None

let (|Printf|_|) (expr: Expr) =
    match expr with
    | PartialPrintf(tpArgs, value, bindArgs) ->
        if List.length bindArgs = List.length tpArgs
        then
            Some (tpArgs, value, bindArgs)
        else
            None
    | _ -> None
