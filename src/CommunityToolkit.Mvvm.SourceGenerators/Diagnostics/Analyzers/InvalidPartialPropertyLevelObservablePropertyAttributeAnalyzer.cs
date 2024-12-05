// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if ROSLYN_4_12_0_OR_GREATER

using System.Collections.Immutable;
using CommunityToolkit.Mvvm.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using static CommunityToolkit.Mvvm.SourceGenerators.Diagnostics.DiagnosticDescriptors;

namespace CommunityToolkit.Mvvm.SourceGenerators;

/// <summary>
/// A diagnostic analyzer that generates an error whenever <c>[ObservableProperty]</c> is used on an invalid partial property declaration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InvalidPartialPropertyLevelObservablePropertyAttributeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        InvalidObservablePropertyDeclarationIsNotIncompletePartialDefinition,
        InvalidObservablePropertyDeclarationReturnsByRef,
        InvalidObservablePropertyDeclarationReturnsRefLikeType);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // This generator is intentionally also analyzing generated code, because Roslyn will interpret properties
        // that have '[GeneratedCode]' on them as being generated (and the same will apply to all partial parts).
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static context =>
        {
            // Get the [ObservableProperty] and [GeneratedCode] symbols
            if (context.Compilation.GetTypeByMetadataName("CommunityToolkit.Mvvm.ComponentModel.ObservablePropertyAttribute") is not INamedTypeSymbol observablePropertySymbol ||
                context.Compilation.GetTypeByMetadataName("System.CodeDom.Compiler.GeneratedCodeAttribute") is not { } generatedCodeAttributeSymbol)
            {
                return;
            }

            context.RegisterSymbolAction(context =>
            {
                // Ensure that we have some target property to analyze (also skip implementation parts)
                if (context.Symbol is not IPropertySymbol { PartialDefinitionPart: null } propertySymbol)
                {
                    return;
                }

                // If the property is not using [ObservableProperty], there's nothing to do
                if (!context.Symbol.TryGetAttributeWithType(observablePropertySymbol, out AttributeData? observablePropertyAttribute))
                {
                    return;
                }

                // Emit an error if the property is not a partial definition with no implementation...
                if (propertySymbol is not { IsPartialDefinition: true, PartialImplementationPart: null })
                {
                    // ...But only if it wasn't actually generated by the [ObservableProperty] generator.
                    bool isImplementationAllowed =
                        propertySymbol is { IsPartialDefinition: true, PartialImplementationPart: IPropertySymbol implementationPartSymbol } &&
                        implementationPartSymbol.TryGetAttributeWithType(generatedCodeAttributeSymbol, out AttributeData? generatedCodeAttributeData) &&
                        generatedCodeAttributeData.TryGetConstructorArgument(0, out string? toolName) &&
                        toolName == typeof(ObservablePropertyGenerator).FullName;

                    // Emit the diagnostic only for cases that were not valid generator outputs
                    if (!isImplementationAllowed)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InvalidObservablePropertyDeclarationIsNotIncompletePartialDefinition,
                            observablePropertyAttribute.GetLocation(),
                            propertySymbol.ContainingType,
                            propertySymbol.Name));
                    }
                }

                // Emit an error if the property returns a value by ref
                if (propertySymbol.ReturnsByRef || propertySymbol.ReturnsByRefReadonly)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidObservablePropertyDeclarationReturnsByRef,
                        observablePropertyAttribute.GetLocation(),
                        propertySymbol.ContainingType,
                        propertySymbol.Name));
                }

                // Emit an error if the property type is a ref struct
                if (propertySymbol.Type.IsRefLikeType)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidObservablePropertyDeclarationReturnsRefLikeType,
                        observablePropertyAttribute.GetLocation(),
                        propertySymbol.ContainingType,
                        propertySymbol.Name));
                }
            }, SymbolKind.Property);
        });
    }
}

#endif