﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Smartstore.Core;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Data;
using Smartstore.Core.Widgets;
using Smartstore.StripeElements.Components;
using Smartstore.StripeElements.Models;
using Smartstore.StripeElements.Providers;
using Smartstore.StripeElements.Settings;
using Smartstore.Web.Models.Checkout;

namespace Smartstore.StripeElements.Filters
{
    public class CheckoutFilter : IAsyncResultFilter
    {
        private readonly SmartDbContext _db;
        private readonly ICommonServices _services;
        private readonly IPaymentService _paymentService;
        private readonly StripeSettings _settings;
        private readonly ICheckoutStateAccessor _checkoutStateAccessor;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWidgetProvider _widgetProvider;

        public CheckoutFilter(
            SmartDbContext db,
            ICommonServices services,
            IPaymentService paymentService,
            StripeSettings settings,
            ICheckoutStateAccessor checkoutStateAccessor,
            IHttpContextAccessor httpContextAccessor,
            IWidgetProvider widgetProvider)
        {
            _db = db;
            _services = services;
            _paymentService = paymentService;
            _settings = settings;
            _checkoutStateAccessor = checkoutStateAccessor;
            _httpContextAccessor = httpContextAccessor;
            _widgetProvider = widgetProvider;
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext filterContext, ResultExecutionDelegate next)
        {
            if (!await IsStripeElementsActive())
            {
                await next();
                return;
            }

            // If api key hasn't been configured yet, don't do anything.
            if (!_settings.SecrectApiKey.HasValue() || !_settings.PublicApiKey.HasValue())
            {
                await next();
                return;
            }

            var checkoutState = _checkoutStateAccessor.CheckoutState.GetCustomState<StripeCheckoutState>();
            var skipPaymentPage = checkoutState.ButtonUsed;
            var customer = _services.WorkContext.CurrentCustomer;

            // TODO: (mh) (core) Make sure we are on payment page.

            // Should only run on a full view rendering result or HTML ContentResult.
            if ((filterContext.Result is StatusCodeResult || filterContext.Result.IsHtmlViewResult()) && skipPaymentPage)
            {
                customer.GenericAttributes.SelectedPaymentMethod = StripeElementsProvider.SystemName;
                await _db.SaveChangesAsync();

                var session = _httpContextAccessor.HttpContext.Session;
                if (!session.ContainsKey("OrderPaymentInfo"))
                {
                    session.TrySetObject("OrderPaymentInfo", new ProcessPaymentRequest
                    {
                        StoreId = _services.StoreContext.CurrentStore.Id,
                        CustomerId = customer.Id,
                        PaymentMethodSystemName = StripeElementsProvider.SystemName
                    });
                }

                // Reset property for backward navigation.
                checkoutState.ButtonUsed = false;

                filterContext.Result = new RedirectToActionResult("Confirm", "Checkout", new { area = string.Empty });
            }

            // TODO: (mh) (core) Make sure we are on confirm page.
            if (customer.GenericAttributes.SelectedPaymentMethod.EqualsNoCase(StripeElementsProvider.SystemName))
            {
                var state = _checkoutStateAccessor.CheckoutState;

                if (state.IsPaymentRequired && await IsStripeElementsActive())
                {
                    _widgetProvider.RegisterWidget("end", 
                        new PartialViewWidget("_CheckoutConfirm", state.GetCustomState<StripeCheckoutState>(), "Smartstore.Stripe"));
                }
            }

            await next();
        }

        private Task<bool> IsStripeElementsActive()
            => _paymentService.IsPaymentMethodActiveAsync(StripeElementsProvider.SystemName, null, _services.StoreContext.CurrentStore.Id);
    }
}