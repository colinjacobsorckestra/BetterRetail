///<reference path='../..//Typings/tsd.d.ts' />
///<reference path='./BaseSingleCheckoutController.ts' />
///<reference path='../Composer.MyAccount/Common/CustomerService.ts' />
///<reference path='../Composer.Cart/CheckoutShippingAddressRegistered/ShippingAddressRegisteredService.ts' />

module Orckestra.Composer {
    'use strict';

    export class ShippingSingleCheckoutController extends Orckestra.Composer.BaseSingleCheckoutController {

        protected customerService: ICustomerService = new CustomerService(new CustomerRepository());
      

        public initialize() {
            super.initialize();
            let self: ShippingSingleCheckoutController = this;
            self.viewModelName = 'ShippingMethod';

            let vueShippingMixin = {
                mounted() {
                    this.calculateSelectedMethod();
                    this.Steps.EnteredOnce.Shipping = this.FulfilledShipping; 

                },
                computed: {
                    FulfilledShipping() {

                        let fulfilledAddress =
                            this.ShippingAddress.FirstName &&
                            this.ShippingAddress.LastName &&
                            this.ShippingAddress.Line1 &&
                            this.ShippingAddress.City &&
                            this.ShippingAddress.RegionCode &&
                            this.ShippingAddress.PostalCode &&
                            this.ShippingAddress.PhoneNumber;

                        if (this.IsAuthenticated) {
                            fulfilledAddress = fulfilledAddress || this.SelectedShippingAddressId
                        }

                        if(this.IsPickUpMethodType) {
                            fulfilledAddress = this.Cart.PickUpLocationId;
                        }

                        return !!(fulfilledAddress && this.Cart.ShippingMethod);
                    },
                    SelectedMethodTypeString() {
                        return this.Cart.ShippingMethod ? this.Cart.ShippingMethod.FulfillmentMethodTypeString : '';
                    },
                    SelectedMethodType() {
                        return this.ShippingMethodTypes.find(type => type.FulfillmentMethodTypeString === this.Cart.ShippingMethod.FulfillmentMethodTypeString);
                    },
                    IsShippingMethodType() {
                        return this.Cart.ShippingMethod &&
                            this.Cart.ShippingMethod.FulfillmentMethodTypeString === FulfillmentMethodTypes.Shipping;
                    },
                    IsPickUpMethodType() {
                        return this.Cart.ShippingMethod &&
                            this.Cart.ShippingMethod.FulfillmentMethodTypeString === FulfillmentMethodTypes.PickUp;
                    },


                },
                methods: {
                    prepareShipping() {
                        if (!this.Cart.ShippingAddress.FirstName && !this.Cart.ShippingAddress.LastName) {
                            this.Cart.ShippingAddress.FirstName = this.Customer.FirstName;
                            this.Cart.ShippingAddress.LastName = this.Customer.LastName;
                        }

                        this.Mode.AddingLine2Address = !this.Cart.ShippingAddress.Line2;
                        this.Mode.AddingNewAddress = false;
                    },
                    clearShippingAddress() {
                        this.Mode.AddingLine2Address = true;
                        let { ShippingAddress: { FirstName, LastName, CountryCode }, Customer } = this.Cart;
                        this.Cart.ShippingAddress = {
                            FirstName: FirstName || Customer.FirstName,
                            LastName: LastName || Customer.LastName,
                            CountryCode
                        };
                    },
                    processShipping() {
                       
                        if (this.IsShippingMethodType) {
                            if (this.IsAuthenticated) {
                                return this.processShippingAddressRegistered();
                            } else {
                                return this.processShippingAddress();
                            }
                        }

                        if (this.IsPickUpMethodType) {
                            return this.processPickUpAddress()
                        }

                        this.Steps.EnteredOnce.Shipping = true;
                        return true;
                    },
                    processBilling() {
                        if (this.IsAuthenticated) {
                            return this.processBillingAddressRegistered();
                        } else {
                            return this.processBillingAddress();
                        }
                    },
                    selectShippingMethod(methodEntity: any) {
                        this.ShippingMethodTypes = this.ShippingMethodTypes.map(x =>
                            x.FulfillmentMethodTypeString === methodEntity.FulfillmentMethodTypeString ? { ...x, SelectedMethod: methodEntity } : x
                        );

                        this.changeMethodsCollapseState(methodEntity.FulfillmentMethodTypeString, 'hide');
                        this.updateShippingMethodProcess(methodEntity);
                    },
                    changeShippingMethodType(e: any) {
                        const { value } = e.target;
                        let shippingMethodType = this.ShippingMethodTypes.find(method =>
                            method.FulfillmentMethodTypeString === value
                        );

                        if (this.Cart.ShippingMethod) {
                            this.changeMethodsCollapseState(this.Cart.ShippingMethod.FulfillmentMethodTypeString, 'hide');
                        }

                        if (!this.debounceUpdateShippingMethod) {
                            this.debounceUpdateShippingMethod = _.debounce(methodType => {
                                this.updateShippingMethodProcess(methodType.SelectedMethod);
                            }, 800);
                        }

                        this.debounceUpdateShippingMethod(shippingMethodType);
                    },

                    changeMethodsCollapseState(shippingMethodType: string, command: string) {
                        let shippingMethodCollapse = $(`#ShippingMethod${shippingMethodType}`);
                        if (shippingMethodCollapse) {
                            shippingMethodCollapse.collapse(command);
                        }
                    },
                    updateShippingMethodProcess(methodEntity: any) {
                        let oldShippingMethod = { ...this.Cart.ShippingMethod };
                        let oldShippingAddress = { ...this.Cart.ShippingAddress };
                        let oldPickUpLocationId = this.Cart.PickUpLocationId;

                        this.Cart.ShippingMethod = methodEntity;

                        if (methodEntity.ShippingProviderId === oldShippingMethod.ShippingProviderId) return;

                        self.checkoutService.updateCart([self.viewModelName])
                            .then(({ Cart }) => {
                                this.Cart.ShippingAddress = oldShippingAddress;
                                this.Cart.PickUpLocationId = oldPickUpLocationId;
                                this.prepareShipping();
                            }).catch(e => {
                                this.Cart.ShippingMethod = oldShippingMethod;
                            })
                    },
                   

                    calculateSelectedMethod() {
                        let selectedProviderId = this.Cart.ShippingMethod ? this.Cart.ShippingMethod.ShippingProviderId : undefined;
                        this.ShippingMethodTypes.forEach(type => {
                            type.IsModified = type.ShippingMethods.length > 1;

                            let selectedInCart = type.ShippingMethods.find(method => method.ShippingProviderId === selectedProviderId);
                            type.SelectedMethod = selectedInCart || type.ShippingMethods.find(method => method.IsSelected)
                        });
                    }
                }
            };

            this.checkoutService.VueCheckoutMixins.push(vueShippingMixin);
        }

        public getUpdateModelPromise(): Q.Promise<any> {
            return Q.fcall(() => {
                let vm = {};
                let { Name, ShippingProviderId } = this.checkoutService.VueCheckout.Cart.ShippingMethod;
                vm[this.viewModelName] = JSON.stringify({ Name, ShippingProviderId });
                return vm;
            });
        }
    }
}
