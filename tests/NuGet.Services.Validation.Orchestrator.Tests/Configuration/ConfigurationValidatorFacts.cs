﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace NuGet.Services.Validation.Orchestrator.Tests
{
    public class ConfigurationValidatorFacts
    {
        [Fact]
        public void ConfigurationValidatorSmokeTest()
        {
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        "TestContentType",
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation1",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation2" }
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation2",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>()
                            }
                        }
                    },
                    {
                        "AnotherContentType",
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation2",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>()
                            }
                        }
                    }
                }
            };

            var ex = Record.Exception(() => Validate(configuration));

            Assert.Null(ex);
        }

        [Fact]
        public void ConfigurationValidatorDetectsDuplicates()
        {
            const string validationName = "Validation1";
            const string badContentType = "BadContentType";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        "GoodContentType",
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>()
                            }
                        }
                    },
                    {
                        badContentType,
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = validationName,
                                TrackAfter = TimeSpan.FromHours(1),
                            },
                            new ValidationConfigurationItem
                            {
                                Name = validationName,
                                TrackAfter = TimeSpan.FromHours(1),
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(configuration));

            Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(validationName, ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(badContentType, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ConfigurationValidatorDetectsUnknownValidationPrerequisites()
        {
            const string NonExistentValidationName = "SomeValidation";
            const string contentType = "SomeContentType";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        contentType,
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation1",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ NonExistentValidationName }
                            },
                        }
                    }
                }
            };

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(configuration));

            Assert.Contains(NonExistentValidationName, ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(contentType, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ConfigurationValidatorDetectsUnknownValidators()
        {
            var validationName = "Validation1";
            const string contentType = "TestContentType";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        contentType,
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = validationName,
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>(),
                            },
                        }
                    }
                }
            };
            var validatorProvider = new Mock<IValidatorProvider>();

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(validatorProvider.Object, configuration));

            Assert.Contains("Validator implementation not found for " + validationName, ex.Message);
            Assert.Contains(contentType, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ConfigurationValidatorDetectsParallelProcessors()
        {
            var validationName1 = "Validation1";
            var validationName2 = "Validation2";
            var processorName1 = "Processor1";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        "ParallelProcessor",
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = validationName1,
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>(),
                            },
                            new ValidationConfigurationItem
                            {
                                Name = validationName2,
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string> { validationName1 },
                            },
                            new ValidationConfigurationItem
                            {
                                Name = processorName1,
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string> { validationName1 },
                            },
                        }
                    }
                }
            };

            var validatorProvider = new Mock<IValidatorProvider>();
            validatorProvider
                .Setup(x => x.IsNuGetValidator(It.Is<string>(n => n == validationName1
                                                          || n == validationName2
                                                          || n == processorName1)))
                .Returns(true);
            validatorProvider
                .Setup(x => x.IsNuGetProcessor(processorName1))
                .Returns(true);

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(validatorProvider.Object, configuration));

            Assert.Contains(
                "The processor Processor1 for ParallelProcessor could run in parallel with Validation2. Processors must not run in parallel with any other validators.",
                ex.Message);
        }

        [Fact]
        public void ConfigurationValidatorAllowsNonParallelProcessors()
        {
            var validationName1 = "Validation1";
            var validationName2 = "Validation2";
            var processorName1 = "Processor1";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        "ParallelProcessor",
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = validationName1,
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>(),
                            },
                            new ValidationConfigurationItem
                            {
                                Name = validationName2,
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string> { validationName1 },
                            },
                            new ValidationConfigurationItem
                            {
                                Name = processorName1,
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string> { validationName2 },
                            },
                        }
                    }
                }
            };

            var validatorProvider = new Mock<IValidatorProvider>();
            validatorProvider
                .Setup(x => x.IsNuGetValidator(It.Is<string>(n => n == validationName1
                                                          || n == validationName2
                                                          || n == processorName1)))
                .Returns(true);
            validatorProvider
                .Setup(x => x.IsNuGetProcessor(processorName1))
                .Returns(true);

            var ex = Record.Exception(() => Validate(validatorProvider.Object, configuration));

            Assert.Null(ex);
        }

        [Fact]
        public void ConfigurationValidatorDetectsLoops()
        {

            const string contentType = "Loops";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        contentType,
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation1",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation2" }
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation2",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation1" }
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(configuration));

            Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(contentType, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ConfigurationValidatorDetectsSelfReferencingValidation()
        {
            const string contentType = "SelfReference";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        contentType,
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation1",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation1" }
                            },
                        }
                    }
                }
            };

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(configuration));

            Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(contentType, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ConfigurationValidatorDetectsSelfReferencingValidation2()
        {
            const string contentType = "SelfReference";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        contentType,
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation1",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation2" }
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation2",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation2" }
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(configuration));

            Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(contentType, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ValidationNamesCantBeEmpty()
        {
            const string contentType = "EmptyValidationName";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        contentType,
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "",
                                TrackAfter = TimeSpan.FromHours(1)
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(configuration));

            Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(contentType, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ValidationTimeoutsCantBeZero()
        {
            const string contentType = "ZeroTimeout";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        contentType,
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "SomeValidation",
                                TrackAfter = TimeSpan.Zero
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(configuration));

            Assert.Contains(nameof(ValidationConfigurationItem.TrackAfter), ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ConfigurationValidatorTreatsDepencyGraphAsOriented()
        {
            /*                       Validation1
             *                         /     \
             *                        /       \
             *                       v         v
             *               Validation3     Validation4
             *                       ^         ^
             *                        \       /
             *                         \     /
             *                       Validation2
             */

            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        "Diamonds",
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation1",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation3", "Validation4" }
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation2",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation3", "Validation4" }
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation3",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>()
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation4",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>()
                            }
                        }
                    }
                }
            };

            var ex = Record.Exception(() => Validate(configuration));

            Assert.Null(ex);
        }

        [Fact]
        public void ConfigurationValidatorTreatsDepencyGraphAsOriented2()
        {
            /*                       Validation1
             *                         /     \
             *                        /       \
             *                       v         v
             *               Validation2     Validation3
             *                       \         /
             *                        \       /
             *                         v     v
             *                       Validation4
             */
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        "Diamonds",
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation1",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation2", "Validation3" }
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation2",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation4" }
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation3",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation4" }
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation4",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>()
                            }
                        }
                    }
                }
            };

            var ex = Record.Exception(() => Validate(configuration));

            Assert.Null(ex);
        }

        [Fact]
        public void ValidationOrderingDoesNotAffectLoopDetection()
        {
            // Validation3 ---> Validation1 ---> Validation2
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        "Loops",
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation1",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation2" }
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation2",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>()
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation3",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation1" }
                            }
                        }
                    }
                }
            };

            var ex = Record.Exception(() => Validate(configuration));

            Assert.Null(ex);
        }

        [Fact]
        public void ConfigurationValidatorBehavesWellOnUnconnectedGraph()
        {
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        "Unconnected",
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation1",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>()
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation2",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>()
                            }
                        }
                    }
                }
            };

            var ex = Record.Exception(() => Validate(configuration));

            Assert.Null(ex);
        }

        [Fact]
        public void ConfigurationValidatorDetectsLoopInUnconnectedGraphs()
        {
            /*  Validation1           Validation2 ---> Validation3
             *                                 ^       /
             *                                  \     /
             *                                   -----
             */
            const string contentType = "Loops";
            var configuration = new ValidationConfiguration()
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        contentType,
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = "Validation1",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>()
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation2",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation3" }
                            },
                            new ValidationConfigurationItem
                            {
                                Name = "Validation3",
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>{ "Validation2" }
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(configuration));

            Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(contentType, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        public static IEnumerable<object[]> FailureBehaviorSettingsCombinations
            => from numIntermediateValidations in Enumerable.Range(1, 10)
               from intermediateValidationsFailureBehavior in (ValidationFailureBehavior[])Enum.GetValues(typeof(ValidationFailureBehavior))
               from lastValidationFailureBehavior in (ValidationFailureBehavior[])Enum.GetValues(typeof(ValidationFailureBehavior))
               select new object[] { lastValidationFailureBehavior, numIntermediateValidations, intermediateValidationsFailureBehavior };

        [Theory]
        [MemberData(nameof(FailureBehaviorSettingsCombinations))]
        public void ConfigurationValidatorDetectsRequiredValidationsThatHasNoChanceToRun(ValidationFailureBehavior lastValidationFailureBehavior, int numIntermediateValidations, ValidationFailureBehavior intermediateValidationsFailureBehavior)
        {
            // If validation that must succeed depends on validation that is not going to be started
            // that validation would have no chance to run, failing all the validations.
            // So we need to detect such situations early

            const string firstValidationName = "FirstValidation";
            const string lastValidationName = "LastValidation";
            const string contentType = "RequiredToRun";
            var configuration = new ValidationConfiguration
            {
                ValidationSteps = new Dictionary<string, List<ValidationConfigurationItem>>
                {
                    {
                        contentType,
                        new List<ValidationConfigurationItem>
                        {
                            new ValidationConfigurationItem
                            {
                                Name = firstValidationName,
                                TrackAfter = TimeSpan.FromHours(1),
                                RequiredValidations = new List<string>(),
                                ShouldStart = false
                            }
                        }
                    }
                }
            };

            var previousValidationName = firstValidationName;
            foreach (var intermediateValidationIndex in Enumerable.Range(1, numIntermediateValidations))
            {
                var intermediateValidationName = $"Validation{intermediateValidationIndex}";
                configuration.ValidationSteps[contentType].Add(new ValidationConfigurationItem
                {
                    Name = intermediateValidationName,
                    TrackAfter = TimeSpan.FromHours(1),
                    RequiredValidations = new List<string> { previousValidationName },
                    ShouldStart = true,
                    FailureBehavior = intermediateValidationsFailureBehavior
                });
                previousValidationName = intermediateValidationName;
            }

            configuration.ValidationSteps[contentType].Add(new ValidationConfigurationItem
            {
                Name = lastValidationName,
                TrackAfter = TimeSpan.FromHours(1),
                RequiredValidations = new List<string> { previousValidationName },
                ShouldStart = true,
                FailureBehavior = lastValidationFailureBehavior
            });

            configuration.ValidationSteps[contentType].Reverse();

            var ex = Assert.Throws<ConfigurationErrorsException>(() => Validate(configuration));

            Assert.Contains(firstValidationName, ex.Message);
            Assert.Contains("cannot be run", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(contentType, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static void Validate(IValidatorProvider validatorProvider, ValidationConfiguration configuration)
        {
            var optionsAccessor = new Mock<IOptionsSnapshot<ValidationConfiguration>>();
            optionsAccessor.SetupGet(cfg => cfg.Value).Returns(configuration);
            var validator = new ConfigurationValidator(validatorProvider, optionsAccessor.Object);
            validator.Validate();
        }

        private static void Validate(ValidationConfiguration configuration)
        {
            var validatorProvider = new Mock<IValidatorProvider>();
            validatorProvider
                .Setup(x => x.IsNuGetValidator(It.IsAny<string>()))
                .Returns(true);

            Validate(validatorProvider.Object, configuration);
        }
    }
}
