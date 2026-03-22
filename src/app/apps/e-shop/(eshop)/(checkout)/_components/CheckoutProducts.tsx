import React, { useState } from "react";
import { Table, Button, Input, InputGroup, InputGroupText } from "reactstrap";
import { IconGift, IconStarFilled, IconX } from "@tabler/icons-react";

const CheckoutProducts = () => {
  const [cartItems, setCartItems] = useState([
    {
      id: 1,
      image: "/images/ecommerce/23.jpg",
      name: "Jacket",
      description: "Sky & stylist",
      size: "Medium",
      items: 2,
      price: 500,
      rating: 3,
    },
    {
      id: 2,
      image: "/images/ecommerce/25.jpg",
      name: "Shoes",
      description: "Trendy & stylist shoes",
      size: "33",
      items: 1,
      price: 350,
      rating: 2,
    },
    {
      id: 3,
      image: "/images/ecommerce/13.jpg",
      name: "Watch",
      description: "Designer golden watch",
      color: "Golden",
      items: 2,
      price: 890,
      rating: 3,
    },
  ]);

  const subtotal = cartItems.reduce(
    (sum, item) => sum + item.price * item.items,
    0
  );
  const estimatedTax = 5.0;
  const shippingHandling = 10.0;
  const total = subtotal + estimatedTax + shippingHandling;

  const handleRemove = (id: number) => {
    const updatedCart = cartItems.filter((item) => item.id !== id);
    setCartItems(updatedCart);
  };
  return (
    <>
      {cartItems.map((item) => (
        <div key={item.id} className="checkout-cart-box d-flex mb-4">
          <div className="cart-images d-flex-center flex-shrink-0">
            <a href="#">
              <img src={item.image} alt={item.name} className="w-80" />
            </a>
          </div>
          <div className="ms-2 flex-grow-1">
            <h6>
              {item.name} - <span>({item.description})</span>
            </h6>
            <div>
              {[...Array(5)].map((_, index) => (
                <IconStarFilled
                  key={index}
                  size={14} // Matches f-s-14
                  className={
                    index < item.rating ? "text-warning" : "star-filled"
                  }
                />
              ))}
            </div>
            <p>
              <span>{item.size ? "Size" : "Color"}</span> :{" "}
              {item.size || item.color}
            </p>
            <p>
              <span>Items</span> : {item.items}
            </p>
          </div>
          <div className="cart-price-box">
            <Button
              color="text-primary"
              onClick={() => handleRemove(item.id)}
              className="p-0"
            >
              <IconX size={18} className="text-secondary" />
            </Button>
            <h5>${item.price}</h5>
          </div>
        </div>
      ))}

      <div className="pricing-details">
        <div className="mb-3">
          <InputGroup>
            <Input placeholder="Enter promo code / Gift Certificate" />
            <InputGroupText className="text-bg-dark">
              <IconGift size={18} />
            </InputGroupText>
          </InputGroup>
        </div>
        <Table className="mb-0">
          <thead>
            <tr>
              <th>Subtotal</th>
              <th className="text-end">${subtotal.toFixed(2)}</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Estimated Tax</td>
              <td className="text-end">${estimatedTax.toFixed(2)}</td>
            </tr>
            <tr>
              <td>Estimated Shipping &amp; Handling</td>
              <td className="text-end">${shippingHandling.toFixed(2)}</td>
            </tr>
            <tr>
              <td>Delivery Charges</td>
              <td className="text-end">-</td>
            </tr>
            <tr>
              <td>Coupon Discount</td>
              <td className="text-end">
                <span className="badge text-light-success">Apply</span>
              </td>
            </tr>
          </tbody>
        </Table>
        <Table className="mb-0">
          <thead>
            <tr>
              <th>Total</th>
              <th className="text-end">${total.toFixed(2)}</th>
            </tr>
          </thead>
        </Table>
        <Button color="primary" className="w-100 mt-3 rounded" id="next">
          Buy Order
        </Button>
      </div>
    </>
  );
};

export default CheckoutProducts;
