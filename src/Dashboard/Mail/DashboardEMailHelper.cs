﻿namespace Dashboard.Mail
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Dashboard.Models;

    using Microsoft.Extensions.Options;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    using SaaSFulfillmentClient;
    using SaaSFulfillmentClient.WebHook;

    using SendGrid;
    using SendGrid.Helpers.Mail;

    public class DashboardEMailHelper : IMarketplaceNotificationHandler
    {
        private const string MailLinkControllerName = "MailLink";

        private readonly IFulfillmentClient fulfillmentClient;

        private readonly DashboardOptions options;

        public DashboardEMailHelper(
            IOptionsMonitor<DashboardOptions> optionsMonitor,
            IFulfillmentClient fulfillmentClient)
        {
            this.fulfillmentClient = fulfillmentClient;
            this.options = optionsMonitor.CurrentValue;
        }

        public async Task ProcessActivateAsync(
            AzureSubscriptionProvisionModel provisionModel,
            CancellationToken cancellationToken = default)
        {
            var queryParams = new List<Tuple<string, string>>
                                  {
                                      new Tuple<string, string>(
                                          "subscriptionId",
                                          provisionModel.SubscriptionId.ToString()),
                                      new Tuple<string, string>("planId", provisionModel.PlanName)
                                  };
            await this.SendEmailAsync(
                () => $"New subscription, {provisionModel.SubscriptionName}",
                () =>
                    $"<p>New subscription. Please take the required action, then return to this email and click the following link to confirm. {this.BuildALink("Activate", queryParams, "Click here to activate subscription")}.</p>"
                    + $"<div> <p> Details are</p> <div> {BuildTable(JObject.Parse(JsonConvert.SerializeObject(provisionModel))) }</div></div>",
                cancellationToken);
        }

        public async Task ProcessChangePlanAsync(
            WebhookPayload payload,
            CancellationToken cancellationToken = default)
        {
            await this.SendWebhookNotificationEmailAsync(
                "Plan change request",
                "Plan change request. Please take the required action, then return to this email and click the following link to confirm.",
                "PlanChange",
                payload,
                cancellationToken);
        }

        public async Task ProcessChangeQuantityAsync(
            WebhookPayload payload,
            CancellationToken cancellationToken = default)
        {
            await this.SendWebhookNotificationEmailAsync(
                "Quantity change request",
                "Quantity change request. Please take the required action, then return to this email and click the following link to confirm.",
                "QuantityChange",
                payload,
                cancellationToken);
        }

        public async Task ProcessOperationFailOrConflictAsync(
            WebhookPayload payload,
            CancellationToken cancellationToken = default)
        {
            var queryParams = new List<Tuple<string, string>>
                                  {
                                      new Tuple<string, string>("subscriptionId", payload.SubscriptionId.ToString())
                                  };

            var subscriptionDetails = await this.fulfillmentClient.GetSubscriptionAsync(
                                          payload.SubscriptionId,
                                          Guid.Empty,
                                          Guid.Empty,
                                          cancellationToken);

            await this.SendEmailAsync(
                () => $"Operation failure, {subscriptionDetails.Name}",
                () =>
                    $"<p>Operation failure. {this.BuildALink("Operations", queryParams, "Click here to list all operations for this subscription", "Subscriptions")}</p>. "
                    + $"<p> Details are {BuildTable(JObject.Parse(JsonConvert.SerializeObject(subscriptionDetails)))}</p>",
                cancellationToken);
        }

        public async Task ProcessReinstatedAsync(
            WebhookPayload payload,
            CancellationToken cancellationToken = default)
        {
            await this.SendWebhookNotificationEmailAsync(
                "Reinstate subscription request",
                "Reinstate subscription request. Please take the required action, then return to this email and click the following link to confirm.",
                "Reinstate",
                payload,
                cancellationToken);
        }

        public async Task ProcessStartProvisioningAsync(AzureSubscriptionProvisionModel provisionModel, CancellationToken cancellationToken = default)
        {
            var queryParams = new List<Tuple<string, string>>
                                  {
                                      new Tuple<string, string>(
                                          "subscriptionId",
                                          provisionModel.SubscriptionId.ToString()),
                                      new Tuple<string, string>("planId", provisionModel.PlanName)
                                  };

            await this.SendEmailAsync(
                () => $"New subscription, {provisionModel.SubscriptionName}",
                () =>
                    $"<p>New subscription. Please take the required action, then return to this email and click the following link to confirm. {this.BuildALink("Update", queryParams, "Click here to activate subscription")}.</p>"
                    + $"<div> <p> Details are</p> <div> {BuildTable(JObject.Parse(JsonConvert.SerializeObject(provisionModel))) }</div></div>",
                cancellationToken);
        }

        public async Task ProcessSuspendedAsync(WebhookPayload payload, CancellationToken cancellationToken = default)
        {
            await this.SendWebhookNotificationEmailAsync(
                "Suspend subscription request",
                "Suspend subscription request. Please take the required action, then return to this email and click the following link to confirm.",
                "SuspendSubscription",
                payload,
                cancellationToken);
        }

        public async Task ProcessUnsubscribedAsync(
            WebhookPayload payload,
            CancellationToken cancellationToken = default)
        {
            await this.SendWebhookNotificationEmailAsync(
                "Cancel subscription request",
                "Cancel subscription request. Please take the required action, then return to this email and click the following link to confirm.",
                "CancelSubscription",
                payload,
                cancellationToken);
        }

        private string BuildALink(
            string controllerAction,
            IEnumerable<Tuple<string, string>> queryParams,
            string innerText,
            string controllerName = MailLinkControllerName)
        {
            var uriStart = FluentUriBuilder.Start(this.options.BaseUrl.Trim()).AddPath(controllerName)
                .AddPath(controllerAction);

            foreach (var (item1, item2) in queryParams)
            {
                uriStart.AddQuery(item1, item2);
            }

            var href = uriStart.Uri.ToString();

            return $"<a href=\"{href}\">{innerText}</a>";
        }

        private string BuildTable(JObject parsed)
        {
            var tableContents = parsed.Properties().AsEnumerable().Select(p => $"<tr><th align=\"left\"> {p.Name} </th><th align=\"left\"> {p.Value}</th></tr>")
                .Aggregate((head, tail) => head + tail);
            return
                $"<table border=\"1\" align=\"left\">{tableContents}</table>";
        }

        private async Task SendEmailAsync(
            Func<string> subjectBuilder,
            Func<string> contentBuilder,
            CancellationToken cancellationToken = default)
        {
            var msg = new SendGridMessage();

            msg.SetFrom(new EmailAddress(this.options.Mail.FromEmail, "Marketplace Dashboard"));

            var recipients = new List<EmailAddress> { new EmailAddress(this.options.Mail.AdminEmail) };

            msg.AddTos(recipients);

            msg.SetSubject(subjectBuilder());

            msg.AddContent(MimeType.Html, contentBuilder());

            var client = new SendGridClient(this.options.Mail.ApiKey);
            var response = await client.SendEmailAsync(msg, cancellationToken);
        }

        private async Task SendWebhookNotificationEmailAsync(
            string subject,
            string mailBody,
            string actionName,
            WebhookPayload payload,
            CancellationToken cancellationToken)
        {
            var queryParams = new List<Tuple<string, string>>
                                  {
                                      new Tuple<string, string>("subscriptionId", payload.SubscriptionId.ToString()),
                                      new Tuple<string, string>("publisherId", payload.PublisherId),
                                      new Tuple<string, string>("offerId", payload.OfferId),
                                      new Tuple<string, string>("planId", payload.PlanId),
                                      new Tuple<string, string>("quantity", payload.Quantity.ToString()),
                                      new Tuple<string, string>("operationId", payload.OperationId.ToString())
                                  };

            var subscriptionDetails = await this.fulfillmentClient.GetSubscriptionAsync(
                                          payload.SubscriptionId,
                                          Guid.Empty,
                                          Guid.Empty,
                                          cancellationToken);

            await this.SendEmailAsync(
                () => $"{subject}, {subscriptionDetails.Name}",
                () => $"<p>{mailBody}" + $"{this.BuildALink(actionName, queryParams, "Click here to confirm.")}</p>"
                                       + $"<br/><div> Details are {BuildTable(JObject.Parse(JsonConvert.SerializeObject(subscriptionDetails)))}</div>",
                cancellationToken);
        }
    }
}