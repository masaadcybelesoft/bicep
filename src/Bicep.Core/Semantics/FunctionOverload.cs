// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Bicep.Core.Syntax;
using Bicep.Core.TypeSystem;

namespace Bicep.Core.Semantics
{
    public class FunctionOverload
    {
        public delegate TypeSymbol ReturnTypeBuilderDelegate(IEnumerable<FunctionArgumentSyntax> arguments);

        public FunctionOverload(string name, TypeSymbol returnType, int minimumArgumentCount, int? maximumArgumentCount, IEnumerable<TypeSymbol> fixedParameterTypes, TypeSymbol? variableParameterType, FunctionFlags flags = FunctionFlags.Default)
            : this(name, args => returnType, returnType, minimumArgumentCount, maximumArgumentCount, fixedParameterTypes, variableParameterType, flags)
        {
        }
        
        public FunctionOverload(string name, ReturnTypeBuilderDelegate returnTypeBuilder, TypeSymbol returnType, int minimumArgumentCount, int? maximumArgumentCount, IEnumerable<TypeSymbol> fixedParameterTypes, TypeSymbol? variableParameterType, FunctionFlags flags = FunctionFlags.Default)
        {
            if (maximumArgumentCount.HasValue && maximumArgumentCount.Value < minimumArgumentCount)
            {
                throw new ArgumentException($"{nameof(maximumArgumentCount.Value)} cannot be less than {nameof(minimumArgumentCount)}.");
            }

            var fixedTypes = fixedParameterTypes.ToImmutableArray();
            if (fixedTypes.Length < minimumArgumentCount && variableParameterType == null)
            {
                throw new ArgumentException("Not enough argument types are specified.");
            }

            this.Name = name;
            this.ReturnTypeBuilder = returnTypeBuilder;
            this.MinimumArgumentCount = minimumArgumentCount;
            this.MaximumArgumentCount = maximumArgumentCount;
            this.FixedParameterTypes = fixedTypes;
            this.VariableParameterType = variableParameterType;
            this.Flags = flags;

            var builder = ImmutableArray.CreateBuilder<string>();
            for (int i = 0; i < fixedTypes.Length; i++)
            {
                builder.Add($"param{i}: {fixedTypes[i]}");
            }

            if (variableParameterType != null)
            {
                builder.Add($"... : {variableParameterType}");
            }

            this.ParameterTypeSignatures = builder.ToImmutable();

            this.TypeSignature = $"({string.Join(", ", this.ParameterTypeSignatures)}): {returnType}";
        }

        public string Name { get; }

        public ImmutableArray<TypeSymbol> FixedParameterTypes { get; }

        public int MinimumArgumentCount { get; }

        public int? MaximumArgumentCount { get; }

        public TypeSymbol? VariableParameterType { get; }

        public ReturnTypeBuilderDelegate ReturnTypeBuilder { get; }

        public FunctionFlags Flags { get; }

        public string TypeSignature { get; }

        public ImmutableArray<string> ParameterTypeSignatures { get; }

        public bool HasParameters => this.MinimumArgumentCount > 0 || this.MaximumArgumentCount > 0;

        public FunctionMatchResult Match(IList<TypeSymbol> argumentTypes, out ArgumentCountMismatch? argumentCountMismatch, out ArgumentTypeMismatch? argumentTypeMismatch)
        {
            argumentCountMismatch = null;
            argumentTypeMismatch = null;

            if (argumentTypes.Count < this.MinimumArgumentCount ||
                (this.MaximumArgumentCount.HasValue && argumentTypes.Count > this.MaximumArgumentCount.Value))
            {
                // Too few or too many arguments.
                argumentCountMismatch = new ArgumentCountMismatch(argumentTypes.Count, this.MinimumArgumentCount, this.MaximumArgumentCount);

                return FunctionMatchResult.Mismatch;
            }

            if (argumentTypes.All(a => a.TypeKind == TypeKind.Any))
            {
                // all argument types are "any"
                // it's a potential match at best
                return FunctionMatchResult.PotentialMatch;
            }

            for (int i = 0; i < argumentTypes.Count; i++)
            {
                var argumentType = argumentTypes[i];
                TypeSymbol expectedType;

                if (i < this.FixedParameterTypes.Length)
                {
                    expectedType = this.FixedParameterTypes[i];
                }
                else
                {
                    if (this.VariableParameterType == null)
                    {
                        // Theoretically this shouldn't happen, becase it already passed argument count checking, either:
                        // - The function takes 0 argument - argumentTypes must be empty, so it won't enter the loop
                        // - The function take at least one argument - when i >= FixedParameterTypes.Length, VariableParameterType
                        //   must not be null, otherwise, the function overload has invalid parameter count definition.
                        throw new ArgumentException($"Got unexpected null value for {nameof(this.VariableParameterType)}. Ensure the function overload definition is correct: '{this.TypeSignature}'.");
                    }

                    expectedType = this.VariableParameterType;
                }

                if (TypeValidator.AreTypesAssignable(argumentType, expectedType) != true)
                {
                    argumentTypeMismatch = new ArgumentTypeMismatch(this, i, argumentType, expectedType);

                    return FunctionMatchResult.Mismatch;
                }
            }

            return FunctionMatchResult.Match;
        }
    }
}
