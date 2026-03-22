import React, { useState } from "react";
import Link from "next/link";
import { Offcanvas, OffcanvasHeader, OffcanvasBody } from "reactstrap";
import { IconEye, IconShoppingCart, IconStarFilled } from "@tabler/icons-react";
import { ShoppingBag, Xmark } from "iconoir-react";
import { cartData } from "@/Data/HeaderMenuData";

type CartItem = {
  id: number;
  name: string;
  imgSrc: string;
  rating: number;
  textColor: string;
  size: string;
  color: string;
  price: number;
  quantity: number;
  href: string;
};

const HeaderCart = () => {
  const [cartItems, setCartItems] = useState<CartItem[]>(cartData);
  const [isOpen, setIsOpen] = useState(false);

  const handleRemoveItem = (id: number) => {
    setCartItems((prev) => prev.filter((item) => item.id !== id));
  };

  const toggle = () => setIsOpen(!isOpen);

  return (
    <>
      <a
        color="link"
        className="head-icon position-relative p-0"
        onClick={toggle}
      >
        <ShoppingBag className="fs-6" />
        <span className="position-absolute translate-middle badge rounded-pill bg-danger badge-notification">
          {cartItems.length}
        </span>
      </a>

      <Offcanvas
        direction="end"
        isOpen={isOpen}
        toggle={toggle}
        className="header-cart-canvas"
      >
        <OffcanvasHeader toggle={toggle}>Cart</OffcanvasHeader>

        <OffcanvasBody className="app-scroll p-0">
          <div className="head-container">
            {cartItems.map((item) => (
              <div
                className="head-box d-flex align-items-start mb-3"
                key={item.id}
              >
                <img
                  alt="cart"
                  className="h-50 me-3 b-r-10"
                  src={item.imgSrc}
                />
                <div className="flex-grow-1">
                  <a
                    className={`text-${item.textColor} mb-0 f-w-600 f-s-16`}
                    href={item.href}
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    {item.name}
                    <span className="text-warning-dark">
                      {" "}
                      ({item.rating}
                      <span className="text-warning f-s-12">
                        <IconStarFilled size={12} />
                      </span>
                      )
                    </span>
                  </a>
                  <div>
                    <span className="text-secondary">
                      <span className="text-dark f-w-500">Size</span>:{" "}
                      {item.size}
                    </span>
                    <span className="text-secondary ms-2">
                      <span className="text-dark f-w-500">Color</span>:{" "}
                      {item.color}
                    </span>
                  </div>
                </div>
                <div className="text-end ms-3">
                  <Xmark
                    className="close-btn cursor-pointer"
                    onClick={() => handleRemoveItem(item.id)}
                  />
                  <p className="text-success f-w-500 mb-0">
                    ${item.price.toFixed(2)} x {item.quantity}
                  </p>
                </div>
              </div>
            ))}

            {cartItems.length === 0 && (
              <div className="hidden-massage py-4 px-3 text-center">
                <img
                  alt="cart"
                  className="img-fluid mb-3 "
                  src="/images/header/cart_empty.gif"
                />
                <div>
                  <h6 className="mb-0">Your Cart is Empty</h6>
                  <p className="text-secondary mb-0">Add some items :)</p>
                  <Link
                    href="/apps/e-shop/product-details"
                    className="btn btn-light-primary btn-xs mt-2"
                  >
                    Shop Now
                  </Link>
                </div>
              </div>
            )}
          </div>
        </OffcanvasBody>

        {cartItems.length > 0 && (
          <div className="offcanvas-footer">
            <div className="head-box-footer p-3">
              <div className="mb-4">
                <h6 className="text-muted f-w-600">
                  Total{" "}
                  <span className="float-end">
                    $
                    {cartItems
                      .reduce(
                        (acc, item) => acc + item.price * item.quantity,
                        0
                      )
                      .toFixed(2)}
                  </span>
                </h6>
              </div>
              <div className="header-cart-btn d-flex gap-2">
                <Link
                  href="/apps/e-shop/cart"
                  className="btn btn-light-primary w-100"
                >
                  <IconEye size={16} /> View Cart
                </Link>
                <Link
                  href="/apps/e-shop/checkout"
                  className="btn btn-light-success w-100"
                >
                  Checkout <IconShoppingCart size={14} />
                </Link>
              </div>
            </div>
          </div>
        )}
      </Offcanvas>
    </>
  );
};

export default HeaderCart;
