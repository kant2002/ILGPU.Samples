﻿// -----------------------------------------------------------------------------
//                                ILGPU Samples
//                   Copyright (c) 2017 ILGPU Samples Project
//                                www.ilgpu.net
//
// File: Program.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details.
// -----------------------------------------------------------------------------

using ILGPU;
using ILGPU.Runtime;
using System;
using System.Linq;

namespace ExplicitlyGroupedKernels
{
    class Program
    {
        /// <summary>
        /// Explicitly-grouped kernels receive an index type (first parameter) of type:
        /// <see cref="GroupedIndex"/>, <see cref="GroupedIndex2"/> or <see cref="GroupedIndex3"/>.
        /// These kernel types expose the underlying blocking/grouping semantics of a GPU
        /// and allow for highly efficient implementation of kernels for different GPUs.
        /// The semantics of theses kernels are equivalent to kernel implementations in CUDA.
        /// An explicitly-grouped kernel can be loaded with:
        /// - LoadImplicitlyGroupedKernel
        /// - LoadAutoGroupedKernel.
        /// 
        /// Note that you must not use warp-shuffle functionality within implicitly grouped
        /// kernels since not all lanes of a warp are guaranteed to participate in the warp shuffle.
        /// </summary>
        /// <param name="index">The current thread index.</param>
        /// <param name="dataView">The view pointing to our memory buffer.</param>
        /// <param name="constant">A nice uniform constant.</param>
        static void GroupedKernel(
            GroupedIndex index,          // The grouped thread index (1D in this case)
            ArrayView<int> dataView,     // A view to a chunk of memory (1D in this case)
            int constant)                // A sample uniform constant
        {
            // Compute the global 1D index for accessing the data view
            var globalIndex = index.ComputeGlobalIndex();

            if (globalIndex < dataView.Length)
                dataView[globalIndex] = globalIndex + constant;

            // Note: this explicitly grouped kernel implements the same functionality
            // as MyKernel in the ImplicitlyGroupedKernels sample.
        }

        /// <summary>
        /// Demonstrates the use of a group-wide barrier.
        /// </summary>
        static void GroupedKernelBarrier(
            GroupedIndex index,          // The grouped thread index (1D in this case)
            ArrayView<int> dataView,     // A view to a chunk of memory (1D in this case)
            ArrayView<int> outputView,   // A view to a chunk of memory (1D in this case)
            int constant)                // A sample uniform constant
        {
            var globalIndex = index.ComputeGlobalIndex();

            // Wait until all threads in the group reach this point
            Group.Barrier();

            if (globalIndex < dataView.Length)
                outputView[globalIndex] = dataView[globalIndex] > constant ? 1 : 0;
        }

        /// <summary>
        /// Demonstrates the use of a group-wide and-barrier.
        /// </summary>
        static void GroupedKernelAndBarrier(
            GroupedIndex index,          // The grouped thread index (1D in this case)
            ArrayView<int> dataView,     // A view to a chunk of memory (1D in this case)
            ArrayView<int> outputView,   // A view to a chunk of memory (1D in this case)
            int constant)              // A sample uniform constant
        {
            // Compute the global 1D index for accessing the data view
            var globalIndex = index.ComputeGlobalIndex();

            // Load value iff the index is in range
            var value = globalIndex < dataView.Length ?
                dataView[globalIndex] :
                constant + 1;

            // Wait until all threads in the group reach this point. Moreover, BarrierAnd
            // evaluates the given predicate and returns true iff the predicate evaluates
            // to true for all threads in the group.
            var found = Group.BarrierAnd(value > constant);

            if (globalIndex < outputView.Length)
                outputView[globalIndex] = found ? 1 : 0;
        }

        /// <summary>
        /// Demonstrates the use of a group-wide or-barrier.
        /// </summary>
        static void GroupedKernelOrBarrier(
            GroupedIndex index,          // The grouped thread index (1D in this case)
            ArrayView<int> dataView,     // A view to a chunk of memory (1D in this case)
            ArrayView<int> outputView,   // A view to a chunk of memory (1D in this case)
            int constant)                // A sample uniform constant
        {
            // Compute the global 1D index for accessing the data view
            var globalIndex = index.ComputeGlobalIndex();

            // Load value iff the index is in range
            var value = globalIndex < dataView.Length ?
                dataView[globalIndex] :
                constant;

            // Wait until all threads in the group reach this point. Moreover, BarrierOr
            // evaluates the given predicate and returns true iff the predicate evaluates
            // to true for any thread in the group.
            var found = Group.BarrierOr(value > constant);

            if (globalIndex < outputView.Length)
                outputView[globalIndex] = found ? 1 : 0;
        }

        /// <summary>
        /// Demonstrates the use of a group-wide popcount-barrier.
        /// </summary>
        static void GroupedKernelPopCountBarrier(
            GroupedIndex index,          // The global thread index (1D in this case)
            ArrayView<int> dataView,     // A view to a chunk of memory (1D in this case)
            ArrayView<int> outputView,   // A view to a chunk of memory (1D in this case)
            int constant)                // A sample uniform constant
        {
            // Compute the global 1D index for accessing the data view
            var globalIndex = index.ComputeGlobalIndex();

            // Load value iff the index is in range
            var value = globalIndex < dataView.Length ?
                dataView[globalIndex] :
                constant;

            // Wait until all threads in the group reach this point. Moreover, BarrierPopCount
            // evaluates the given predicate and returns the number of threads in the group
            // for which the predicate evaluated to true.
            var count = Group.BarrierPopCount(value > constant);

            if (globalIndex < outputView.Length)
                outputView[globalIndex] = count;
        }

        /// <summary>
        /// Launches a simple 1D kernel using the default explicit-grouping functionality.
        /// </summary>
        static void Main(string[] args)
        {
            // Create main context
            using (var context = new Context())
            {
                // For each available accelerator...
                foreach (var acceleratorId in Accelerator.Accelerators)
                {
                    // Create default accelerator for the given accelerator id
                    using (var accelerator = Accelerator.Create(context, acceleratorId))
                    {
                        Console.WriteLine($"Performing operations on {accelerator}");

                        var data = Enumerable.Range(1, 128).ToArray();

                        var groupSize = accelerator.MaxNumThreadsPerGroup;
                        var launchDimension = new GroupedIndex(
                            (data.Length + groupSize - 1) / groupSize,  // Compute the number of groups (round up)
                            groupSize);                                 // Use the given group size

                        using (var dataSource = accelerator.Allocate<int>(data.Length))
                        {
                            // Initialize data source
                            dataSource.CopyFrom(data, 0, 0, data.Length);

                            using (var dataTarget = accelerator.Allocate<int>(data.Length))
                            {

                                // Launch default grouped kernel
                                {
                                    dataTarget.MemSetToZero();

                                    var groupedKernel = accelerator.LoadStreamKernel<GroupedIndex, ArrayView<int>, int>(GroupedKernel);
                                    groupedKernel(launchDimension, dataTarget.View, 64);

                                    accelerator.Synchronize();

                                    Console.WriteLine("Default grouped kernel");
                                    var target = dataTarget.GetAsArray();
                                    for (int i = 0, e = target.Length; i < e; ++i)
                                        Console.WriteLine($"Data[{i}] = {target[i]}");
                                }

                                // Launch grouped kernel with barrier
                                {
                                    dataTarget.MemSetToZero();

                                    var groupedKernel = accelerator.LoadStreamKernel<GroupedIndex, ArrayView<int>, ArrayView<int>, int>(GroupedKernelBarrier);
                                    groupedKernel(launchDimension, dataSource, dataTarget.View, 64);

                                    accelerator.Synchronize();

                                    Console.WriteLine("Grouped-barrier kernel");
                                    var target = dataTarget.GetAsArray();
                                    for (int i = 0, e = target.Length; i < e; ++i)
                                        Console.WriteLine($"Data[{i}] = {target[i]}");
                                }

                                // Launch grouped kernel with and-barrier
                                {
                                    dataTarget.MemSetToZero();

                                    var groupedKernel = accelerator.LoadStreamKernel<GroupedIndex, ArrayView<int>, ArrayView<int>, int>(GroupedKernelAndBarrier);
                                    groupedKernel(launchDimension, dataSource, dataTarget.View, 0);

                                    accelerator.Synchronize();

                                    Console.WriteLine("Grouped-and-barrier kernel");
                                    var target = dataTarget.GetAsArray();
                                    for (int i = 0, e = target.Length; i < e; ++i)
                                        Console.WriteLine($"Data[{i}] = {target[i]}");
                                }

                                // Launch grouped kernel with or-barrier
                                {
                                    dataTarget.MemSetToZero();

                                    var groupedKernel = accelerator.LoadStreamKernel<GroupedIndex, ArrayView<int>, ArrayView<int>, int>(GroupedKernelOrBarrier);
                                    groupedKernel(launchDimension, dataSource, dataTarget.View, 64);

                                    accelerator.Synchronize();

                                    Console.WriteLine("Grouped-or-barrier kernel");
                                    var target = dataTarget.GetAsArray();
                                    for (int i = 0, e = target.Length; i < e; ++i)
                                        Console.WriteLine($"Data[{i}] = {target[i]}");
                                }

                                // Launch grouped kernel with popcount-barrier
                                {
                                    dataTarget.MemSetToZero();

                                    var groupedKernel = accelerator.LoadStreamKernel<GroupedIndex, ArrayView<int>, ArrayView<int>, int>(GroupedKernelPopCountBarrier);
                                    groupedKernel(launchDimension, dataSource, dataTarget.View, 0);

                                    accelerator.Synchronize();

                                    Console.WriteLine("Grouped-popcount-barrier kernel");
                                    var target = dataTarget.GetAsArray();
                                    for (int i = 0, e = target.Length; i < e; ++i)
                                        Console.WriteLine($"Data[{i}] = {target[i]}");
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
