///<reference path='../../../../Composer.UI/Source/Typings/tsd.d.ts' />
///<reference path='../../../../Composer.UI/Source/Typescript/Mvc/ComposerClient.ts' />
///<reference path='../../../../Composer.MyAccount.UI/Common/Source/Typescript/CustomerService.ts' />
///<reference path='../../../../Composer.Cart.UI/CheckoutCommon/Source/Typescript/AddressDto.ts' />

module Orckestra.Composer {
    'use strict';

    export class RecurringCartAddressRegisteredService {

        protected customerService: ICustomerService;

        public constructor(customerService: ICustomerService) {
            this.customerService = customerService;
        }

         /**
         * Get the customer addresses. The selected billing/shipping address is taken from the cart by default.
         * If no address has been set in the cart, the selected billing/shipping address corresponds to the preferred address.
         */
        public getRecurringCartAddresses(cart: any): Q.Promise<any> {

            if (!cart) {
                throw new Error('The cart is required');
            }

            return this.customerService.getRecurringCartAddresses(cart.Name)
                .then(addresses => {
                    addresses.AddressesLoaded = true;
                    addresses.SelectedBillingAddressId = this.getSelectedBillingAddressId(cart, addresses);
                    addresses.SelectedShippingAddressId = this.getSelectedShippingAddressId(cart, addresses);

                    return addresses;
                });
        }

        public getSelectedBillingAddressId(cart: any, addressList: any) : string {

            if (this.isBillingAddressFromCartValid(cart, addressList)) {
                return cart.Payment.BillingAddress.Id;
            }

            return this.getPreferredBillingAddressId(addressList);
        }

        private isBillingAddressFromCartValid(cart: any, addressList: any) : boolean {

            if (cart.Payment === undefined) {
                return false;
            }

            if (cart.Payment.BillingAddress === undefined) {
                return false;
            }

            return _.any(addressList.Addresses, (address: AddressDto) => address.Id === cart.Payment.BillingAddress.Id);
        }

        private getPreferredBillingAddressId(addressList: any) : string {

            var preferredBillingAddress = _.find(addressList.Addresses, (address: AddressDto) => address.IsPreferredBilling);

            return preferredBillingAddress === undefined ? undefined : preferredBillingAddress.Id;
        }

        public getSelectedShippingAddressId(cart: any, addressList: any) : string {

            if (this.isShippingAddressFromCartValid(cart, addressList)) {
                return cart.ShippingAddress.Id;
            }

            return this.getPreferredShippingAddressId(addressList);
        }

        private isShippingAddressFromCartValid(cart: any, addressList: any) : boolean {

            if (cart.ShippingAddress === undefined) {
                return false;
            }

            return _.any(addressList.Addresses, (address: AddressDto) => address.Id === cart.ShippingAddress.Id);
        }

        private getPreferredShippingAddressId(addressList: any) : string {

            var preferredShippingAddress = _.find(addressList.Addresses, (address: AddressDto) => address.IsPreferredShipping);

            return preferredShippingAddress === undefined ? undefined : preferredShippingAddress.Id;
        }
    }
}
