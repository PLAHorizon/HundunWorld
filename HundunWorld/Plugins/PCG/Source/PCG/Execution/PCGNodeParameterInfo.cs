// PCG Plugin - Authored parameter schema
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §4.5 (parameter validation) and §6.2.

using System;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Execution
{
    /// <summary>
    /// Declares one positional parameter slot of a node type: what it is called, what kind of value it
    /// holds, and what the node does when the slot is empty.
    /// </summary>
    /// <remarks>
    /// Node parameters are stored as a positional <c>object[]</c> rather than a name-keyed dictionary,
    /// because the graph editor is built on <c>VisjectSurface</c>, whose node values are also positional
    /// (docs/PCG_PLUGIN_DESIGN.md §6.1). Positional storage is compact and diff-friendly, but on its own it
    /// means nothing detects a value authored into the wrong slot: the node's own reader falls back to its
    /// default, so a <c>Vector3</c> parked in a float slot silently produces default behaviour and the graph
    /// looks like it ignores the user. This schema is what lets the compiler catch that at compile time and
    /// what lets the editor label each field and seed a newly placed node.
    /// </remarks>
    public sealed class PCGNodeParameterInfo
    {
        /// <summary>Name shown in the editor and used to target the slot from an exposed parameter.</summary>
        public string Name;

        /// <summary>Kind of value the slot expects.</summary>
        public PCGParameterType Kind;

        /// <summary>
        /// Value used when the graph stores nothing at this index. Must agree with the fallback the node
        /// itself passes to its parameter reader, or the editor would show one value and run another.
        /// </summary>
        public object Default;

        /// <summary>Choice labels for <see cref="PCGParameterType.Enum"/> slots, index-aligned with the enum.</summary>
        public string[] Options;

        /// <summary>Optional one-line help for the editor tooltip.</summary>
        public string Description;

        /// <summary>Creates a parameter description.</summary>
        public PCGNodeParameterInfo(string name, PCGParameterType kind, object defaultValue = null,
            string[] options = null, string description = null)
        {
            Name = name;
            Kind = kind;
            Default = defaultValue;
            Options = options;
            Description = description;
        }

        /// <inheritdoc />
        public override string ToString() => $"{Name}:{Kind}{(Options != null ? $" [{Options.Length} option(s)]" : string.Empty)}";

        /// <summary>
        /// True when a stored value can reasonably be read as this slot's kind.
        /// </summary>
        /// <remarks>
        /// Deliberately mirrors what the node readers actually accept: numbers convert among each other, an
        /// enum value or label is accepted for an enum slot, and a string that parses as a Guid is accepted
        /// for an asset slot because graphs round-trip through JSON. Anything else is refused so the compiler
        /// can say so instead of the node quietly using its default.
        /// </remarks>
        public bool IsCompatible(object value)
        {
            if (value == null)
                return true;

            switch (Kind)
            {
            case PCGParameterType.Float:
                return value is float || value is double || value is int || value is long;
            case PCGParameterType.Int:
                return value is int || value is long || value is float || value is double;
            case PCGParameterType.Bool:
                return value is bool;
            case PCGParameterType.Vector:
                return value is Vector3 || value is Float3;
            case PCGParameterType.Color:
                return value is Color;
            case PCGParameterType.Asset:
                return value is Guid || (value is string s && Guid.TryParse(s, out _));
            case PCGParameterType.String:
                return value is string;
            case PCGParameterType.Enum:
                return value is int || value is Enum || value is string;
            default:
                return false;
            }
        }
    }
}
