namespace GraphBLAS.FSharp.Backend.COOMatrix

open Brahma.FSharp.OpenCL
open OpenCL.Net
open Brahma.OpenCL
open GraphBLAS.FSharp.Backend.Common
open GraphBLAS.FSharp.Backend.COOMatrix.Utilities
open Microsoft.FSharp.Quotations

module internal rec EWiseAdd =
    let run (gpu:GPU) (matrixLeft: COOMatrix<'a>) (matrixRight: COOMatrix<'a>) (op: Expr<'a -> 'a -> 'a>) =
        if matrixLeft.Values.Length = 0 then
            let resultRows = matrixRight.Rows
            let resultColumns = matrixRight.Columns
            let resultValues = matrixRight.Values

            {
                RowCount = matrixRight.RowCount
                ColumnCount = matrixRight.ColumnCount
                Rows = resultRows
                Columns = resultColumns
                Values = resultValues
            }

        elif matrixRight.Values.Length = 0 then
            let resultRows = matrixLeft.Rows
            let resultColumns = matrixLeft.Columns
            let resultValues = matrixLeft.Values

            {
                RowCount = matrixLeft.RowCount
                ColumnCount = matrixLeft.ColumnCount
                Rows = resultRows
                Columns = resultColumns
                Values = resultValues
            }

        else
            let _do = gpuEWiseAdd gpu op
            _do matrixLeft matrixRight 

    let gpuEWiseAdd (gpu:GPU) (op: Expr<'a -> 'a -> 'a>) =

        let processor = gpu.GetNewProcessor ()

        let sw = System.Diagnostics.Stopwatch()
        let sw2 = System.Diagnostics.Stopwatch()
        let sw3 = System.Diagnostics.Stopwatch()
        let sw4 = System.Diagnostics.Stopwatch()

        sw4.Start()
        let merge = GraphBLAS.FSharp.Backend.COOMatrix.Utilities.Merge.merge gpu
        let preparePositions = GraphBLAS.FSharp.Backend.COOMatrix.Utilities.PreparePositions.preparePositions gpu op
        let setPositions = GraphBLAS.FSharp.Backend.COOMatrix.Utilities.SetPositions.setPositions<'a> gpu
        sw4.Stop()
        
        printfn "Compilation: %A" (sw4.ElapsedMilliseconds)

        fun (matrixLeft: COOMatrix<'a>) (matrixRight: COOMatrix<'a>) ->
            sw.Reset()
            sw2.Reset()
            sw3.Reset()
            //sw4.Reset()

            sw.Start()
            sw2.Start()

            use matrixLeftRows = gpu.Allocate<_>(matrixLeft.Rows.Length, matrixLeft.Rows, Brahma.OpenCL.Operations.ReadOnly)
            use matrixLeftColumns = gpu.Allocate<_>(matrixLeft.Columns.Length, matrixLeft.Columns, Brahma.OpenCL.Operations.ReadOnly)
            use matrixLeftValues = gpu.Allocate<'a>(matrixLeft.Values.Length, matrixLeft.Values, Brahma.OpenCL.Operations.ReadOnly)
            use matrixRightRows = gpu.Allocate<_>(matrixRight.Rows.Length, matrixRight.Rows, Brahma.OpenCL.Operations.ReadOnly)
            use matrixRightColumns = gpu.Allocate<_>(matrixRight.Columns.Length, matrixRight.Columns, Brahma.OpenCL.Operations.ReadOnly)
            use matrixRightValues = gpu.Allocate<'a>(matrixRight.Values.Length, matrixRight.Values, Brahma.OpenCL.Operations.ReadOnly)

            //processor.Post(Msg.CreateToGPUMsg<_>(matrixLeft.Rows, matrixLeftRows))
            //processor.Post(Msg.CreateToGPUMsg<_>(matrixLeft.Columns, matrixLeftColumns))
            //processor.Post(Msg.CreateToGPUMsg<_>(matrixLeft.Values, matrixLeftValues))

            //processor.Post(Msg.CreateToGPUMsg<_>(matrixRight.Rows, matrixRightRows))
            //processor.Post(Msg.CreateToGPUMsg<_>(matrixRight.Columns, matrixRightColumns))
            //processor.Post(Msg.CreateToGPUMsg<_>(matrixRight.Values, matrixRightValues))

            sw2.Stop()

            let allRows, allColumns, allValues =
                merge
                    processor
                    matrixLeftRows matrixLeftColumns matrixLeftValues
                    matrixRightRows matrixRightColumns matrixRightValues

    
            use rawPositions = preparePositions processor allRows allColumns allValues

            let resultRows, resultColumns, resultValues, resultLength = setPositions processor allRows allColumns allValues rawPositions

            sw3.Start()

            let rows = Array.zeroCreate resultLength
            let columns = Array.zeroCreate resultLength
            let values = Array.zeroCreate resultLength
            processor.Post(Msg.CreateToHostMsg(ToHost<int>(resultRows, rows)))
            processor.Post(Msg.CreateToHostMsg(ToHost<int>(resultColumns, columns)))
            processor.PostAndReply(fun ch -> Msg.CreateToHostMsg(ToHost<'a>(resultValues, values, ch)))

            sw3.Stop()
            sw.Stop()
            
            printfn "Data to gpu: %A" (sw2.ElapsedMilliseconds)
            printfn "Data to host: %A" (sw3.ElapsedMilliseconds)
            printfn "Elapsed total: %A" (sw.ElapsedMilliseconds)

            //processor.Post(Msg.CreateFreeMsg<_>(matrixLeftRows))
            //processor.Post(Msg.CreateFreeMsg<_>(matrixLeftColumns))
            //processor.Post(Msg.CreateFreeMsg<_>(matrixLeftValues))
            //processor.Post(Msg.CreateFreeMsg<_>(matrixRightRows))
            //processor.Post(Msg.CreateFreeMsg<_>(matrixRightColumns))
            //processor.Post(Msg.CreateFreeMsg<_>(matrixRightValues))
            processor.Post(Msg.CreateFreeMsg<_>(resultRows))
            processor.Post(Msg.CreateFreeMsg<_>(resultColumns))
            processor.Post(Msg.CreateFreeMsg<_>(resultValues))
            processor.Post(Msg.CreateFreeMsg<_>(allRows))
            processor.Post(Msg.CreateFreeMsg<_>(allColumns))
            processor.Post(Msg.CreateFreeMsg<_>(allValues))
            //processor.Post(Msg.CreateFreeMsg<_>(rawPositions))

            {
                RowCount = matrixLeft.RowCount
                ColumnCount = matrixLeft.ColumnCount
                Rows = rows
                Columns = columns
                Values = values
            }
