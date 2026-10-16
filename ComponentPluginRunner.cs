using System.Runtime.InteropServices;
using System.Text;

namespace WasmCSharp;

public sealed class ComponentPluginRunner
{
    private const string NativeLibrary = "wasmtime";
    private const byte ComponentValueKindU8 = 2;
    private const byte ComponentValueKindF64 = 10;
    private const byte ComponentValueKindString = 12;
    private const byte ComponentValueKindList = 13;
    private const byte ComponentValueKindRecord = 14;
    private const byte ComponentValueKindVariant = 16;
    private const byte ComponentValueKindResult = 19;

    public Task<DataFrame> ParseAsync(
        string componentPath,
        ReadOnlyMemory<byte> input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentPath);
        cancellationToken.ThrowIfCancellationRequested();

        var inputBytes = input.ToArray();
        var result = Parse(componentPath, inputBytes);
        cancellationToken.ThrowIfCancellationRequested();
        result.Validate();
        return Task.FromResult(result);
    }

    private static DataFrame Parse(string componentPath, byte[] input)
    {
        var config = Native.wasm_config_new();
        if (config == IntPtr.Zero) throw new InvalidOperationException("Could not create Wasmtime config.");
        Native.wasmtime_config_wasm_component_model_set(config, true);
            var engine = Native.wasm_engine_new_with_config(config);
            if (engine == IntPtr.Zero) throw new InvalidOperationException("Could not create Wasmtime engine.");
            try
            {
                var componentBytes = File.ReadAllBytes(componentPath);
                var componentBuffer = Marshal.AllocHGlobal(componentBytes.Length);
                try
                {
                    Marshal.Copy(componentBytes, 0, componentBuffer, componentBytes.Length);
                    Check(Native.wasmtime_component_new(engine, componentBuffer, (nuint)componentBytes.Length, out var component));
                    try
                    {
                        var store = Native.wasmtime_store_new(engine, IntPtr.Zero, IntPtr.Zero);
                        if (store == IntPtr.Zero) throw new InvalidOperationException("Could not create Wasmtime store.");
                        try
                        {
                            var context = Native.wasmtime_store_context(store);
                            var linker = Native.wasmtime_component_linker_new(engine);
                            if (linker == IntPtr.Zero) throw new InvalidOperationException("Could not create component linker.");
                            try
                            {
                                var instance = default(ComponentInstance);
                                Check(Native.wasmtime_component_linker_instantiate(linker, context, component, ref instance));
                                const string interfaceName = "csv:plugin/importer@0.1.0";
                                var interfaceNamePointer = Utf8(interfaceName);
                                try
                                {
                                    var interfaceIndex = Native.wasmtime_component_instance_get_export_index(
                                        ref instance, context, IntPtr.Zero, interfaceNamePointer,
                                        (nuint)Encoding.UTF8.GetByteCount(interfaceName));
                                    if (interfaceIndex == IntPtr.Zero)
                                    {
                                        throw new InvalidDataException($"Component does not export {interfaceName}.");
                                    }

                                    try
                                    {
                                        var parseNamePointer = Utf8("parse");
                                        try
                                        {
                                            var parseIndex = Native.wasmtime_component_instance_get_export_index(
                                                ref instance, context, interfaceIndex, parseNamePointer, 5);
                                            if (parseIndex == IntPtr.Zero)
                                            {
                                                throw new InvalidDataException("Component does not export importer.parse.");
                                            }

                                            try
                                            {
                                                if (!Native.wasmtime_component_instance_get_func(
                                                        ref instance, context, parseIndex, out var function))
                                                {
                                                    throw new InvalidDataException("The component parse export is not a function.");
                                                }

                                                return InvokeParse(function, context, input);
                                            }
                                            finally
                                            {
                                                Native.wasmtime_component_export_index_delete(parseIndex);
                                            }
                                        }
                                        finally
                                        {
                                            Marshal.FreeCoTaskMem(parseNamePointer);
                                        }
                                    }
                                    finally
                                    {
                                        Native.wasmtime_component_export_index_delete(interfaceIndex);
                                    }
                                }
                                finally
                                {
                                    Marshal.FreeCoTaskMem(interfaceNamePointer);
                                }
                            }
                            finally
                            {
                                Native.wasmtime_component_linker_delete(linker);
                            }
                        }
                        finally
                        {
                            Native.wasmtime_store_delete(store);
                        }
                    }
                    finally
                    {
                        Native.wasmtime_component_delete(component);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(componentBuffer);
                }
            }
            finally
            {
                Native.wasm_engine_delete(engine);
            }
    }

    private static DataFrame InvokeParse(ComponentFunc function, IntPtr context, byte[] input)
    {
        var elementSize = Marshal.SizeOf<ComponentValue>();
        var elementBuffer = Marshal.AllocHGlobal(Math.Max(1, input.Length * elementSize));
        var argsBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<ComponentValue>());
        try
        {
            for (var index = 0; index < input.Length; index++)
            {
                var value = new ComponentValue { Kind = ComponentValueKindU8 };
                value.Of.U8 = input[index];
                Marshal.StructureToPtr(value, IntPtr.Add(elementBuffer, index * elementSize), false);
            }

            var argument = new ComponentValue { Kind = ComponentValueKindList };
            argument.Of.ListSize = (nuint)input.Length;
            argument.Of.ListData = elementBuffer;
            Marshal.StructureToPtr(argument, argsBuffer, false);

            var result = default(ComponentValue);
            Check(Native.wasmtime_component_func_call(
                ref function,
                context,
                argsBuffer,
                1,
                ref result,
                1));
            try
            {
                if (result.Kind != ComponentValueKindResult)
                {
                    throw new InvalidDataException($"Plugin returned unexpected value kind {result.Kind}.");
                }

                if (result.Of.ResultIsOk == 0)
                {
                    var error = result.Of.ResultValue == IntPtr.Zero
                        ? "Plugin returned an unknown parse error."
                        : ReadParseError(result.Of.ResultValue);
                    throw new InvalidDataException(error);
                }

                if (result.Of.ResultValue == IntPtr.Zero)
                {
                    throw new InvalidDataException("Plugin returned an empty dataframe result.");
                }

                return ReadDataFrame(result.Of.ResultValue);
            }
            finally
            {
                Native.wasmtime_component_val_delete(ref result);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(argsBuffer);
            Marshal.FreeHGlobal(elementBuffer);
        }
    }

    private static string ReadParseError(IntPtr value)
    {
        var variant = Marshal.PtrToStructure<ComponentValue>(value);
        if (variant.Kind != ComponentValueKindVariant || variant.Of.VariantValue == IntPtr.Zero)
        {
            return "Plugin returned a malformed parse error.";
        }

        var caseName = ReadName(new ComponentName
        {
            Size = variant.Of.VariantDiscriminantSize,
            Data = variant.Of.VariantDiscriminantData
        });
        var messageValue = Marshal.PtrToStructure<ComponentValue>(variant.Of.VariantValue);
        var message = ReadString(messageValue);
        return $"{caseName}: {message}";
    }

    private static DataFrame ReadDataFrame(IntPtr value)
    {
        var record = ReadRecord(Marshal.PtrToStructure<ComponentValue>(value));
        var columns = ReadList(FindField(record, "columns"))
            .Select(columnValue =>
            {
                var column = ReadRecord(columnValue);
                return ReadString(FindField(column, "name"));
            })
            .ToArray();

        var rows = ReadList(FindField(record, "rows"))
            .Select(rowValue =>
            {
                var row = ReadRecord(rowValue);
                return new DataFrameRow(
                    ReadString(FindField(row, "timestamp")),
                    ReadList(FindField(row, "values"))
                        .Select(ReadF64)
                        .ToArray());
            })
            .ToArray();

        return new DataFrame(columns, rows);
    }

    private static ComponentValue FindField(IReadOnlyDictionary<string, ComponentValue> record, string name)
    {
        return record.TryGetValue(name, out var value)
            ? value
            : throw new InvalidDataException($"Plugin result is missing dataframe field '{name}'.");
    }

    private static IReadOnlyDictionary<string, ComponentValue> ReadRecord(ComponentValue value)
    {
        if (value.Kind != ComponentValueKindRecord)
        {
            throw new InvalidDataException($"Expected a record, got component value kind {value.Kind}.");
        }

        var entries = new Dictionary<string, ComponentValue>(StringComparer.Ordinal);
        var entrySize = Marshal.SizeOf<ComponentRecordEntry>();
        for (nuint index = 0; index < value.Of.RecordSize; index++)
        {
            var entry = Marshal.PtrToStructure<ComponentRecordEntry>(
                IntPtr.Add(value.Of.RecordData, checked((int)(index * (nuint)entrySize))));
            entries[ReadName(entry.Name)] = entry.Value;
        }

        return entries;
    }

    private static IReadOnlyList<ComponentValue> ReadList(ComponentValue value)
    {
        if (value.Kind != ComponentValueKindList)
        {
            throw new InvalidDataException($"Expected a list, got component value kind {value.Kind}.");
        }

        var values = new ComponentValue[checked((int)value.Of.ListSize)];
        var elementSize = Marshal.SizeOf<ComponentValue>();
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = Marshal.PtrToStructure<ComponentValue>(
                IntPtr.Add(value.Of.ListData, index * elementSize));
        }

        return values;
    }

    private static string ReadString(ComponentValue value)
    {
        if (value.Kind != ComponentValueKindString)
        {
            throw new InvalidDataException($"Expected a string, got component value kind {value.Kind}.");
        }

        return ReadName(new ComponentName
        {
            Size = value.Of.StringSize,
            Data = value.Of.StringData
        });
    }

    private static double ReadF64(ComponentValue value)
    {
        if (value.Kind != ComponentValueKindF64)
        {
            throw new InvalidDataException($"Expected an f64, got component value kind {value.Kind}.");
        }

        return value.Of.F64;
    }

    private static string ReadName(ComponentName name)
    {
        if (name.Data == IntPtr.Zero || name.Size == 0) return string.Empty;
        var bytes = new byte[checked((int)name.Size)];
        Marshal.Copy(name.Data, bytes, 0, bytes.Length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static IntPtr Utf8(string value)
    {
        return Marshal.StringToCoTaskMemUTF8(value);
    }

    private static void Check(IntPtr error)
    {
        if (error == IntPtr.Zero) return;
        try
        {
            var message = default(ComponentName);
            Native.wasmtime_error_message(error, ref message);
            var errorMessage = ReadName(message);
            Native.wasm_byte_vec_delete(ref message);
            throw new InvalidOperationException(errorMessage);
        }
        finally
        {
            Native.wasmtime_error_delete(error);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComponentName
    {
        public nuint Size;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct ComponentValueUnion
    {
        [FieldOffset(0)] public byte U8;
        [FieldOffset(0)] public double F64;
        [FieldOffset(0)] public nuint StringSize;
        [FieldOffset(8)] public IntPtr StringData;
        [FieldOffset(0)] public nuint ListSize;
        [FieldOffset(8)] public IntPtr ListData;
        [FieldOffset(0)] public nuint RecordSize;
        [FieldOffset(8)] public IntPtr RecordData;
        [FieldOffset(0)] public nuint VariantDiscriminantSize;
        [FieldOffset(8)] public IntPtr VariantDiscriminantData;
        [FieldOffset(16)] public IntPtr VariantValue;
        [FieldOffset(0)] public byte ResultIsOk;
        [FieldOffset(8)] public IntPtr ResultValue;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct ComponentValue
    {
        [FieldOffset(0)] public byte Kind;
        [FieldOffset(8)] public ComponentValueUnion Of;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComponentRecordEntry
    {
        public ComponentName Name;
        public ComponentValue Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComponentInstance
    {
        public ulong StoreId;
        public uint Private1;
        public uint Private2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComponentFunc
    {
        public ulong StoreId;
        public uint Private1;
        public uint PrivatePadding;
        public uint Private2;
    }

    private static class Native
    {
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wasm_config_new();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wasm_config_delete(IntPtr config);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "wasmtime_config_wasm_component_model_set")]
        internal static extern void wasmtime_config_wasm_component_model_set(
            IntPtr config, [MarshalAs(UnmanagedType.I1)] bool enabled);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wasm_engine_new_with_config(IntPtr config);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wasm_engine_delete(IntPtr engine);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wasmtime_store_new(IntPtr engine, IntPtr data, IntPtr finalizer);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wasmtime_store_context(IntPtr store);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wasmtime_store_delete(IntPtr store);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wasmtime_component_new(
            IntPtr engine, IntPtr bytes, nuint length, out IntPtr component);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wasmtime_component_delete(IntPtr component);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wasmtime_component_get_export_index(
            IntPtr component, IntPtr parent, IntPtr name, nuint nameLength);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wasmtime_component_linker_new(IntPtr engine);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wasmtime_component_linker_delete(IntPtr linker);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wasmtime_component_linker_instantiate(
            IntPtr linker, IntPtr context, IntPtr component, ref ComponentInstance instance);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wasmtime_component_instance_get_export_index(
            ref ComponentInstance instance, IntPtr context, IntPtr parent,
            IntPtr name, nuint nameLength);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wasmtime_component_export_index_delete(IntPtr index);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool wasmtime_component_instance_get_func(
            ref ComponentInstance instance, IntPtr context, IntPtr index, out ComponentFunc function);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr wasmtime_component_func_call(
            ref ComponentFunc function, IntPtr context, IntPtr args, nuint argsSize,
            ref ComponentValue results, nuint resultsSize);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wasmtime_component_val_delete(ref ComponentValue value);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wasmtime_error_message(IntPtr error, ref ComponentName message);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wasmtime_error_delete(IntPtr error);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void wasm_byte_vec_delete(ref ComponentName value);
    }
}
