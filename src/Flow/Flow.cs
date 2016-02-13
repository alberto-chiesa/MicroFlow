﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace MicroFlow
{
  public abstract class Flow
  {
    public abstract string Name { get; }

    public Task Run()
    {
      var flowBuilder = new FlowBuilder();
      Build(flowBuilder);
      var flowDescription = flowBuilder.CreateFlow();

      var validators = GetStandardValidators();
      ConfigureValidation(validators);

      var validationResult = ValidateFlow(validators, flowDescription);
      if (validationResult.HasErrors)
      {
        throw new FlowValidationException("Flow is not valid")
        {
          ValidatonResult = validationResult
        };
      }

      new DefaultHandlersSetter(flowDescription).Execute();

      var runner = new FlowRunner();

      var services = new ServiceCollection();
      ConfigureServices(services);

      runner.WithServices(services);

      ILogger log = CreateFlowExecutionLogger() ?? new NullLogger();
      runner.WithLogger(log);

      try
      {
        Debug.Assert(flowDescription.InitialNode != null);

        log.Info("Starting the flow '{0}'", Name);

        Task task = runner.Run(flowDescription);

        Debug.Assert(task != null);

        Task continuation = task.ContinueWith(t =>
        {
          // ReSharper disable once AccessToDisposedClosure
          runner.Dispose();
          flowBuilder.Clear();

          if (t.IsFaulted)
          {
            Debug.Assert(t.Exception != null);

            log.Exception("Unhandled exception", t.Exception);
            log.Info("Flow '{0}' is terminated due to an unhandled exception", Name);

            return TaskHelper.FromException(t.Exception);
          }

          if (t.IsCanceled)
          {
            log.Info("Flow '{0}' is cancelled", Name);

            return TaskHelper.CancelledTask;
          }

          log.Info("Flow '{0}' is completed", Name);

          return TaskHelper.CompletedTask;
        }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();

        return continuation;
      }
      catch (Exception ex)
      {
        runner.Dispose();
        flowBuilder.Clear();

        log.Exception("Unhandled exception", ex);
        log.Info("Flow '{0}' is terminated due to an unhandled exception", Name);

        return TaskHelper.FromException(ex);
      }
    }

    [NotNull]
    public ValidationResult Validate()
    {
      var flowBuilder = new FlowBuilder();

      try
      {
        Build(flowBuilder);

        var validators = GetStandardValidators();
        ConfigureValidation(validators);

        return ValidateFlow(validators, flowBuilder.CreateFlow());
      }
      finally
      {
        flowBuilder.Clear();
      }
    }

    protected abstract void Build([NotNull] FlowBuilder builder);

    protected virtual void ConfigureServices([NotNull] IServiceCollection services)
    {
    }

    protected virtual void ConfigureValidation([NotNull] IValidatorCollection validators)
    {
    }

    [CanBeNull]
    protected virtual ILogger CreateFlowExecutionLogger()
    {
      return null;
    }

    private static ValidationResult ValidateFlow(
      [NotNull] ValidatorCollection validators,
      [NotNull] FlowDescription flowDescription)
    {
      var validationResult = new ValidationResult();

      foreach (FlowValidator validator in validators)
      {
        if (!validator.Validate(flowDescription))
        {
          validationResult.TakeErrorsFrom(validator.Result);
        }
      }

      return validationResult;
    }

    [NotNull]
    private static ValidatorCollection GetStandardValidators()
    {
      return new ValidatorCollection
      {
        new ConnectionValidator(),
        new ReachabilityValidator(),
        new ActivityInitializationValidator(),
        new ActivityTypeValidator()
      };
    }
  }
}