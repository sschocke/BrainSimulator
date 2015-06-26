﻿using BrainSimulator.Memory;
using BrainSimulator.Nodes;
using BrainSimulator.Task;
using BrainSimulator.Transforms;
using BrainSimulator.Utils;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YAXLib;
using ManagedCuda.CudaBlas;

namespace BrainSimulator.Matrix
{
    /// <summary>
    /// Operations that are allowed to run using the Matrix Node
    /// </summary>
    [Flags]
    public enum MatOperation
    {
        None = 0,
        Addition = 1,
        Multiplication = 1 << 2,

        DotProd = 1 << 6,
        MultiplElemntWise = 1 << 7,
        Substraction = 1 << 8,

        MinIndex = 1 << 10,
        MaxIndex = 1 << 11,

        GetCol = 1 << 14,
        GetRow = 1 << 15,

        Minus = 1 << 18,


        Normalize = 1 << 21,
        Norm2 = 1 << 22,


        EuclidDist = 1 << 25,
        CosDist = 1 << 26,

        Exp = 1 << 29,
        Log = 1 << 30,

        Abs = 1 << 41,
        Floor = 1 << 42,
        Round = 1 << 43,
        Ceil = 1 << 44,

        Copy = 1 << 33
    }



    /// <summary>
    /// Strategy DesignPatern:
    ///    This is the abstract class that defines what will happen, then specific instance (that depends on the execuion=operation type (CPU/GPU/cublas..)) will execute the queried operation
    /// </summary>
    public abstract class MyMatrixOps
    {
        protected MyWorkingNode callee;


        public abstract void Run(MatOperation operation, MyMemoryBlock<float> A, MyMemoryBlock<float> B, MyMemoryBlock<float> Result); 
        public abstract void Run(MatOperation operation, MyMemoryBlock<float> A, MyMemoryBlock<float> Result); 
        public abstract void Run(MatOperation operation, MyMemoryBlock<float> A, float value, MyMemoryBlock<float> Result);
        public abstract void Run(MatOperation operation, MyMemoryBlock<float> A); // change A


        public static MatOperation AvailableOperations(){
            return (MatOperation)0;
        }





        public float RunReturn(MatOperation operation, MyMemoryBlock<float> A, MyMemoryBlock<float> B, MyMemoryBlock<float> Result)
        {
            Run(operation, A, B, Result);
            Result.SafeCopyToHost();
            return Result.Host[0];
        }
        public float RunReturn(MatOperation operation, MyMemoryBlock<float> A, MyMemoryBlock<float> Result)
        {
            Run(operation, A, Result);
            Result.SafeCopyToHost();
            return Result.Host[0];
        }



        public static MyMemoryBlock<float> SetupResultSize(MatOperation operation, MyMemoryBlock<float> A, MyMemoryBlock<float> B, MyMemoryBlock<float> Result)
        {
            Result.Count = A != null ? A.Count : 1;
            Result.ColumnHint = A != null ? A.ColumnHint : 1;

            if (A != null)
            {
                if (operation == MatOperation.DotProd)
                {
                    Result.Count = Result.ColumnHint = 1;
                }
                else if (operation == MatOperation.Multiplication)
                {
                    if (A != null && B != null && A.ColumnHint != 0 && B.Count > 1)
                    {
                        Result.ColumnHint = B.ColumnHint;
                        Result.Count = B.ColumnHint * A.Count / A.ColumnHint;
                    }
                }
                else if (operation == MatOperation.GetCol)
                {
                    Result.Count = A.Count / A.ColumnHint;
                    Result.ColumnHint = Result.Count;
                }
                else if (operation == MatOperation.GetRow)
                {
                    Result.Count = A.ColumnHint;
                    Result.ColumnHint = Result.Count;
                }
                else if (B!=null && (operation == MatOperation.MultiplElemntWise || operation == MatOperation.Addition))
                {
                    Result.ColumnHint = Math.Max(A.ColumnHint, B.ColumnHint);
                    Result.Count = Math.Max(A.Count, B.Count);
                }
            }
            return Result;
        }


        public static bool Validate(MatOperation operation, MyMemoryBlock<float> A, MyMemoryBlock<float> B, MyMemoryBlock<float> Result)
        {
            if (A == null || Result == null)
                return false;
            bool is_it_correct = true;

            if (operation == MatOperation.DotProd)
            {
                is_it_correct = (A.Count == B.Count) && (Result.Count == 1);
            }
            else if (operation == MatOperation.Multiplication)
            { // it should allow MAT*MAT, vec*MAT and MAT*vec , in correct sizes of course
                if (B == null)
                {
                    is_it_correct = A.Count == Result.Count && A.ColumnHint == Result.ColumnHint;
                }
                else
                {
                    is_it_correct = (A.ColumnHint == B.Count / B.ColumnHint) && (B.ColumnHint == Result.ColumnHint) && (A.Count / A.ColumnHint == Result.Count / Result.ColumnHint);
                    is_it_correct = is_it_correct || (B.Count == 1) || (A.Count == 1); // it still allows A*5 :-)
                }
            }
            else if (operation == MatOperation.Addition || operation == MatOperation.MultiplElemntWise)
            {
                if (B == null)
                {
                    is_it_correct = A.Count == Result.Count && A.ColumnHint == Result.ColumnHint;
                }
                else
                {
                    is_it_correct = (A.Count == B.Count) && (A.ColumnHint == B.ColumnHint);  /// same size
                    is_it_correct |= A.ColumnHint == B.ColumnHint || A.Count / A.ColumnHint == B.Count / B.ColumnHint; /// same # of colums, rows
                    is_it_correct |= A.Count == 1 || B.Count == 1;
                    is_it_correct |= (Math.Max(A.Count, B.Count) == Result.Count) && (Math.Max(A.ColumnHint, B.ColumnHint) == Result.ColumnHint);
                }
            }
            return is_it_correct;
        
        }




    }
}