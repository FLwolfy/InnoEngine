using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Rendering.Shaders;

namespace Inno.Build.Toolchains.Bgfx.Tools;

internal sealed partial class BgfxShaderIrGenerator
{
    private bool EmitControlFlow(ShaderIrInstruction instruction)
    {
        if (instruction.operation is not (ShaderIrOperation.Branch or ShaderIrOperation.Loop)) return false;
        foreach (ShaderIrValue output in instruction.outputs)
            m_body.Append("    ").Append(Declaration(output.type, NewLocal(output))).AppendLine(";");
        if (instruction.operation == ShaderIrOperation.Branch)
        {
            m_body.Append("    if (").Append(Value(instruction.inputs[0])).AppendLine(") {");
            EmitRegion(instruction.regions[0]);
            AssignRegionOutputs(instruction);
            m_body.AppendLine("    } else {");
            EmitRegion(instruction.regions[1]);
            AssignRegionOutputs(instruction, 1);
            m_body.AppendLine("    }");
            return true;
        }

        for (int index = 0; index < instruction.outputs.Count; index++)
            Assign(m_body, instruction.outputs[index].type, Value(instruction.outputs[index]), Value(instruction.inputs[index + 1]));
        ShaderIrBlock region = instruction.regions[0];
        ShaderIrInstruction iteration = region.instructions.Single(static value => value.operation == ShaderIrOperation.RegionInput && value.inputName == "iteration");
        string counter = NewLocal(iteration.outputs[0]);
        m_body.Append("    for (uint ").Append(counter).Append(" = 0u; ").Append(counter).Append(" < ")
            .Append(Value(instruction.inputs[0])).Append("; ++").Append(counter).AppendLine(") {");
        ShaderIrInstruction[] carried = region.instructions.Where(static value => value.operation == ShaderIrOperation.RegionInput && value.inputName!.StartsWith("state.", StringComparison.Ordinal))
            .OrderBy(static value => value.inputName, StringComparer.Ordinal).ToArray();
        for (int index = 0; index < carried.Length; index++)
        {
            ShaderIrValue parameter = carried[index].outputs[0];
            string snapshot = NewLocal(parameter);
            // Copy every carried value before evaluating the body. Assigning final state then remains
            // simultaneous even when the body swaps two values or returns a carried value directly.
            m_body.Append("    ").Append(Declaration(parameter.type, snapshot)).AppendLine(";");
            Assign(m_body, parameter.type, snapshot, Value(instruction.outputs[index]));
        }
        EmitRegion(region);
        AssignRegionOutputs(instruction);
        m_body.AppendLine("    }");
        return true;
    }

    private void EmitRegion(ShaderIrBlock region)
    {
        foreach (ShaderIrInstruction instruction in region.instructions)
            if (instruction.operation != ShaderIrOperation.RegionInput) EmitInstruction(instruction);
    }

    private void AssignRegionOutputs(ShaderIrInstruction instruction, int regionIndex = 0)
    {
        int index = 0;
        foreach (KeyValuePair<string, ShaderIrValue> pair in instruction.regions[regionIndex].outputs.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            ShaderIrValue output = instruction.outputs[index++];
            Assign(m_body, output.type, Value(output), Value(pair.Value));
        }
    }
}
