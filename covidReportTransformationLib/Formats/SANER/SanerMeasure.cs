﻿// <copyright file="SanerMeasure.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>
using System;
using System.Collections.Generic;
using System.Text;
using covidReportTransformationLib.Formats.CDC;
using covidReportTransformationLib.Formats.FEMA;
using covidReportTransformationLib.Utils;
using Hl7.Fhir.Model;
using static covidReportTransformationLib.Formats.SANER.SanerCommon;

namespace covidReportTransformationLib.Formats.SANER
{
    /// <summary>A SANER measure generator.</summary>
    public static class SanerMeasure
    {
        /// <summary>True once initialization is complete.</summary>
        private static bool _initialized = false;

        /// <summary>The measures.</summary>
        private static readonly Dictionary<string, Measure> _measures = new Dictionary<string, Measure>();

        /// <summary>Builds a measure.</summary>
        /// <exception cref="ArgumentNullException">Thrown when one or more required arguments are null.</exception>
        /// <param name="format">Describes the format to use.</param>
        /// <returns>A Measure.</returns>
        private static Measure BuildMeasure(
            IReportingFormat format)
        {
            if (format == null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            if (string.IsNullOrEmpty(format.Name))
            {
                throw new ArgumentNullException(nameof(format), $"Invalid IReportingFormat.Name: {format.Name}");
            }

            if ((format.Fields == null) || (format.Fields.Count == 0))
            {
                throw new ArgumentNullException(nameof(format), $"Invalid IReportingFormat.Fields: {format.Fields}");
            }

            if ((format.MeasureGroupings == null) || (format.MeasureGroupings.Count == 0))
            {
                throw new ArgumentNullException(nameof(format), $"Invalid IReportingFormat.MeasureGroupings: {format.MeasureGroupings}");
            }

            Measure measure = new Measure()
            {
                Meta = new Meta()
                {
                    Profile = new string[]
                    {
                        "http://hl7.org/fhir/4.0/StructureDefinition/Measure",
                        "http://hl7.org/fhir/us/saner/StructureDefinition/PublicHealthMeasure",
                    },
                },
                Id = format.Name,
                Name = format.Name,
                Url = $"{CanonicalUrl}/{format.Name}",
                Version = MeasureVersion,
                Title = format.Title,
                Description = new Markdown(format.Description),
                Status = PublicationStatus.Draft,
                Experimental = true,
                Subject = new CodeableConcept("Location", "Location"),
                Date = PublicationDate,
                Publisher = Publisher,
                Jurisdiction = new List<CodeableConcept>()
                {
                    FhirTriplet.UnitedStates.GetConcept(),
                },
                UseContext = new List<UsageContext>()
                {
                    new UsageContext()
                    {
                        Code = FhirTriplet.GetCode(FhirSystems.UsageContextType, CommonLiterals.ContextFocus),
                        Value = FhirTriplet.SctCovid.GetConcept(),
                    },
                },
                Type = new List<CodeableConcept>()
                {
                    FhirTriplet.MeasureTypeComposite.GetConcept(),
                },
                Group = new List<Measure.GroupComponent>(),
                Contact = SanerCommon.Contacts,
                Author = format.Authors,
            };

            if ((format.Definition != null) && (format.Definition.Count > 0))
            {
                measure.Definition = new List<Markdown>();
                foreach (string definition in format.Definition)
                {
                    measure.Definition.Add(new Markdown(definition));
                }
            }

            if (format.Artifacts != null)
            {
                measure.RelatedArtifact = new List<RelatedArtifact>();
                measure.RelatedArtifact.AddRange(format.Artifacts);
            }

            foreach (MeasureGrouping grouping in format.MeasureGroupings)
            {
                if (grouping.CodeCoding != null)
                {
                    measure.Group.Add(GroupComponentFromNested(grouping, format));
                    continue;
                }

                if ((!string.IsNullOrEmpty(grouping.FieldName)) &&
                    format.Fields.ContainsKey(grouping.FieldName))
                {
                    measure.Group.Add(GroupComponentFromFlat(grouping, format));
                }
            }

            return measure;
        }

        private static Measure.GroupComponent GroupComponentFromFlat(
            MeasureGrouping grouping,
            IReportingFormat format)
        {
            FormatField field = format.Fields[grouping.FieldName];

            string title = field.Title ?? field.Name;
            string description = field.Description ?? title;

            Measure.GroupComponent groupComponent = new Measure.GroupComponent()
            {
                Code = new CodeableConcept(
                            SanerCommon.CanonicalUrl,
                            field.Name,
                            description),
            };

            if ((grouping.GroupAttributes != null) && (grouping.GroupAttributes.Count > 0))
            {
                Extension groupAttributes = new Extension()
                {
                    Url = "http://hl7.org/fhir/us/saner/StructureDefinition/MeasureGroupAttributes",
                    Extension = new List<Extension>(),
                };

                foreach (MeasureGroupingExtension ext in grouping.GroupAttributes)
                {
                    Extension attribute = new Extension()
                    {
                        Url = ext.Key,
                    };

                    if ((ext.Properties != null) && (ext.Properties.Count > 0))
                    {
                        CodeableConcept value = new CodeableConcept()
                        {
                            Coding = new List<Coding>(),
                        };

                        foreach (FhirTriplet prop in ext.Properties)
                        {
                            value.Coding.Add(prop.GetCoding());
                        }

                        if (!string.IsNullOrEmpty(ext.Text))
                        {
                            value.Text = ext.Text;
                        }

                        attribute.Value = value;
                    }

                    if (!string.IsNullOrEmpty(ext.ValueString))
                    {
                        attribute.Value = new FhirString(ext.ValueString);
                    }

                    groupAttributes.Extension.Add(attribute);
                }

                groupComponent.Extension = new List<Extension>()
                    {
                        groupAttributes,
                    };
            }

            if ((grouping.PopulationFields != null) && (grouping.PopulationFields.Count > 0))
            {
                groupComponent.Population = new List<Measure.PopulationComponent>();

                foreach (MeasureGroupingPopulation pop in grouping.PopulationFields)
                {
                    if (!format.Fields.ContainsKey(pop.Name))
                    {
                        continue;
                    }

                    FormatField popField = format.Fields[pop.Name];

                    Measure.PopulationComponent populationComponent = new Measure.PopulationComponent()
                    {
                        Code = new FhirTriplet(
                            FhirSystems.SanerPopulation,
                            popField.Name,
                            popField.Title).GetConcept(popField.Description),
                        Description = field.Description,
                        Criteria = new Expression()
                        {
                            Description = popField.Title,
                            Language = "text/plain",
                            Expression_ = popField.Description,
                        },
                    };

                    if (pop.PopulationType != null)
                    {
                        populationComponent.Code.Coding.Add(pop.PopulationType.GetCoding());
                    }

                    groupComponent.Population.Add(populationComponent);
                }
            }
            else if (field.Type == FormatField.FieldType.Boolean)
            {
                groupComponent.Population = new List<Measure.PopulationComponent>();

                Measure.PopulationComponent trueComponent = new Measure.PopulationComponent()
                {
                    Code = new FhirTriplet(
                        FhirSystems.SanerAggregateBool,
                        "true",
                        "Count of 'Yes' or 'True' responses for this field").GetConcept(),
                    Description = $"YES - {field.Description ?? field.Title}",
                    Criteria = new Expression()
                    {
                        Description = field.Title,
                        Language = "text/plain",
                        Expression_ = "true",
                    },
                };

                Measure.PopulationComponent falseComponent = new Measure.PopulationComponent()
                {
                    Code = new FhirTriplet(
                        FhirSystems.SanerAggregateBool,
                        "false",
                        "Count of 'No' or 'False' responses for this field").GetConcept(),
                    Description = $"NO - {field.Description ?? field.Title}",
                    Criteria = new Expression()
                    {
                        Description = field.Title,
                        Language = "text/plain",
                        Expression_ = "false",
                    },
                };

                groupComponent.Population.Add(trueComponent);
                groupComponent.Population.Add(falseComponent);
            }
            else if (field.Type == FormatField.FieldType.Choice)
            {
                groupComponent.Population = new List<Measure.PopulationComponent>();

                foreach (FormatFieldOption populationChoice in field.Options)
                {
                    Measure.PopulationComponent choiceComponent = new Measure.PopulationComponent()
                    {
                        Code = new FhirTriplet(
                            FhirSystems.SanerAggregateChoice,
                            populationChoice.Text,
                            $"Aggregate count of respondents selecting the option: {populationChoice.Text}").GetConcept(),
                        Description = $"{field.Title}:{populationChoice.Text}",
                        Criteria = new Expression()
                        {
                            Description = field.Title,
                            Language = "text/plain",
                            Expression_ = populationChoice.Text,
                        },
                    };

                    groupComponent.Population.Add(choiceComponent);
                }
            }

            return groupComponent;
        }

        /// <summary>Group component from nested.</summary>
        /// <param name="grouping">The grouping.</param>
        /// <param name="format">  Describes the format to use.</param>
        /// <returns>A Measure.GroupComponent.</returns>
        private static Measure.GroupComponent GroupComponentFromNested(
            MeasureGrouping grouping,
            IReportingFormat format)
        {
            Measure.GroupComponent groupComponent = new Measure.GroupComponent()
            {
                Code = grouping.CodeCoding.GetConcept(grouping.CodeText),
            };

            if ((grouping.GroupAttributes != null) && (grouping.GroupAttributes.Count > 0))
            {
                Extension groupAttributes = new Extension()
                {
                    Url = "http://hl7.org/fhir/us/saner/StructureDefinition/MeasureGroupAttributes",
                    Extension = new List<Extension>(),
                };

                foreach (MeasureGroupingExtension ext in grouping.GroupAttributes)
                {
                    Extension attribute = new Extension()
                    {
                        Url = ext.Key,
                    };

                    if ((ext.Properties != null) && (ext.Properties.Count > 0))
                    {
                        CodeableConcept value = new CodeableConcept()
                        {
                            Coding = new List<Coding>(),
                        };

                        foreach (FhirTriplet prop in ext.Properties)
                        {
                            value.Coding.Add(prop.GetCoding());
                        }

                        if (!string.IsNullOrEmpty(ext.Text))
                        {
                            value.Text = ext.Text;
                        }

                        attribute.Value = value;
                    }

                    if (!string.IsNullOrEmpty(ext.ValueString))
                    {
                        attribute.Value = new FhirString(ext.ValueString);
                    }

                    groupAttributes.Extension.Add(attribute);
                }

                groupComponent.Extension = new List<Extension>()
                    {
                        groupAttributes,
                    };
            }

            if ((grouping.PopulationFields != null) && (grouping.PopulationFields.Count > 0))
            {
                groupComponent.Population = new List<Measure.PopulationComponent>();

                foreach (MeasureGroupingPopulation pop in grouping.PopulationFields)
                {
                    if (!format.Fields.ContainsKey(pop.Name))
                    {
                        continue;
                    }

                    FormatField field = format.Fields[pop.Name];

                    Measure.PopulationComponent populationComponent = new Measure.PopulationComponent()
                    {
                        Code = new FhirTriplet(
                            FhirSystems.SanerPopulation,
                            field.Name,
                            field.Title).GetConcept(field.Description),
                        Description = field.Description,
                        Criteria = new Expression()
                        {
                            Description = field.Title,
                            Language = "text/plain",
                            Expression_ = field.Description,
                        },
                    };

                    if (pop.PopulationType != null)
                    {
                        populationComponent.Code.Coding.Add(pop.PopulationType.GetCoding());
                    }

                    groupComponent.Population.Add(populationComponent);
                }
            }

            return groupComponent;
        }

        /// <summary>Initializes this object.</summary>
        public static void Init()
        {
            if (_initialized)
            {
                return;
            }

            _measures.Add(AcutePatientImpact.Current.Name, BuildMeasure(AcutePatientImpact.Current));
            _measures.Add(AcuteHealthcareWorker.Current.Name, BuildMeasure(AcuteHealthcareWorker.Current));
            _measures.Add(AcuteHealthcareSupply.Current.Name, BuildMeasure(AcuteHealthcareSupply.Current));
            _measures.Add(DailyReporting.Current.Name, BuildMeasure(DailyReporting.Current));

            _initialized = true;
        }

        /// <summary>Cdc grouped measure.</summary>
        /// <returns>A Measure.</returns>
        public static Measure CDCPatientImpactMeasure()
        {
            if (!_initialized)
            {
                Init();
            }

            return _measures[AcutePatientImpact.Current.Name];
        }

        /// <summary>Cdc patient impact bundle.</summary>
        /// <returns>A Bundle.</returns>
        public static Bundle CDCPatientImpactBundle()
        {
            if (!_initialized)
            {
                Init();
            }

            return GetBundleForMeasure(
                _measures[AcutePatientImpact.Current.Name],
                AcutePatientImpact.Current.Name);
        }

        /// <summary>Cdc healthcare worker bundle.</summary>
        /// <returns>A Bundle.</returns>
        public static Bundle CDCHealthcareWorkerBundle()
        {
            if (!_initialized)
            {
                Init();
            }

            return GetBundleForMeasure(
                _measures[AcuteHealthcareWorker.Current.Name],
                AcuteHealthcareWorker.Current.Name);
        }

        /// <summary>Cdc healthcare supply bundle.</summary>
        /// <returns>A Bundle.</returns>
        public static Bundle CDCHealthcareSupplyBundle()
        {
            if (!_initialized)
            {
                Init();
            }

            return GetBundleForMeasure(
                _measures[AcuteHealthcareSupply.Current.Name],
                AcuteHealthcareSupply.Current.Name);
        }

        /// <summary>Fema complete measure.</summary>
        /// <returns>A Measure.</returns>
        public static Measure FEMADailyMeasure()
        {
            if (!_initialized)
            {
                Init();
            }

            return _measures[DailyReporting.Current.Name];
        }

        /// <summary>Fema daily bundle.</summary>
        /// <returns>A Bundle.</returns>
        public static Bundle FEMADailyBundle()
        {
            if (!_initialized)
            {
                Init();
            }

            return GetBundleForMeasure(
                _measures[DailyReporting.Current.Name],
                DailyReporting.Current.Name);
        }

        /// <summary>Gets bundle for measure.</summary>
        /// <param name="measure">The measure.</param>
        /// <param name="id">     The identifier.</param>
        /// <returns>The bundle for measure.</returns>
        private static Bundle GetBundleForMeasure(
            Measure measure,
            string id)
        {
            string bundleId = FhirIds.NextId;

            Bundle bundle = new Bundle()
            {
                Meta = new Meta()
                {
                    Profile = new string[]
                    {
                        "http://hl7.org/fhir/4.0/StructureDefinition/Bundle",
                    },
                },
                Id = bundleId,
                Identifier = FhirIds.IdentifierForId(bundleId),
                Type = Bundle.BundleType.Collection,
                Timestamp = new DateTimeOffset(DateTime.Now),
            };

            bundle.Entry = new List<Bundle.EntryComponent>();

            bundle.AddResourceEntry(
                measure,
                $"{FhirSystems.Internal}MeasureReport/{id}");

            return bundle;
        }
    }
}
