"use client";
import React, { useState } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Row,
  Table,
} from "reactstrap";
import {
  IconAward,
  IconCalendarMonth,
  IconChecks,
  IconCreditCard,
  IconDeviceMobile,
  IconEdit,
  IconFileInvoice,
  IconMail,
  IconPackage,
  IconShoppingCart,
  IconStack2,
  IconStarFilled,
  IconTrash,
  IconTruckDelivery,
} from "@tabler/icons-react";

const OrdersDetailsPage = () => {
  const [orders, setOrders] = useState([
    {
      id: 1,
      productName: "Men's Sports Shoes",
      productImage: "/images/ecommerce/11.jpg",
      color: "White",
      size: "Small",
      status: "CANCELLED",
      price: "$452.00",
      orderDate: "5 Apr,2024 6:05 PM",
      quantity: 3,
      rating: 2,
    },
    {
      id: 2,
      productName: "Watch",
      productImage: "/images/ecommerce/18.jpg",
      color: "Black",
      size: "XL",
      status: "DELIVERED",
      price: "$988.00",
      orderDate: "5 Sep,2024 5:11 PM",
      quantity: 2,
      rating: 2,
    },
    {
      id: 3,
      productName: "T-shirt",
      productImage: "/images/ecommerce/19.jpg",
      color: "Green",
      size: "Medium",
      status: "PICKUPS",
      price: "$452.00",
      orderDate: "15 Oct,2024 1:51 PM",
      quantity: 3,
      rating: 3,
    },
    {
      id: 4,
      productName: "Shoes for Men",
      productImage: "/images/ecommerce/28.jpg",
      color: "Black",
      size: "Small",
      status: "DELIVERED",
      price: "$400.00",
      orderDate: "19 Oct,2024 6:55 PM",
      quantity: 1,
      rating: 3,
    },
  ]);

  const handleDelete = (id: number) => {
    const updatedOrders = orders.filter((order) => order.id !== id);
    setOrders(updatedOrders); // Update the state to remove the deleted order
  };
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Orders Details"
          title="Apps"
          path={["E-shop", "Orders Details"]}
          Icon={IconStack2}
        />

        <Row className="order-details">
          <Col xxl={9}>
            <Row>
              <Col lg={4}>
                <Card className="order-details-card">
                  <CardHeader>
                    <h5 className="text-nowrap">Order Details (#12311)</h5>
                  </CardHeader>
                  <CardBody>
                    <div className="d-flex justify-content-between">
                      <h6 className="f-w-600 text-dark">
                        <IconCalendarMonth
                          size={18}
                          className=" me-2 text-secondary"
                        />
                        Date
                      </h6>
                      <div className="text-end">
                        <p>01/1/2024</p>
                      </div>
                    </div>
                    <div className="d-flex justify-content-between mt-3">
                      <h6 className="f-w-600 text-dark">
                        <IconCreditCard
                          size={18}
                          className=" me-2 text-secondary"
                        />
                        Payment
                      </h6>
                      <div className="text-end">
                        <p>Online</p>
                      </div>
                    </div>
                    <div className="d-flex justify-content-between mt-3">
                      <h6 className="f-w-600 text-dark">
                        <IconTruckDelivery
                          size={18}
                          className=" me-2 text-secondary"
                        />
                        Shipping
                      </h6>
                      <div className="text-end">
                        <p> Fast Shipping</p>
                      </div>
                    </div>
                  </CardBody>
                </Card>
              </Col>

              <Col lg={4}>
                <Card className="order-details-card">
                  <CardHeader>
                    <h5>Customer Details</h5>
                  </CardHeader>
                  <CardBody>
                    <div className="d-flex justify-content-between">
                      <h6 className="f-w-600 text-dark">
                        <IconFileInvoice
                          size={18}
                          className=" me-2 text-secondary"
                        />
                        Customer
                      </h6>
                      <div className="text-end">
                        <p>Chris Coyier</p>
                      </div>
                    </div>
                    <div className="d-flex justify-content-between mt-3">
                      <h6 className="f-w-600 text-dark">
                        <IconMail size={18} className=" me-2 text-secondary" />
                        Email
                      </h6>
                      <div className="text-end">
                        <p>dam@1234.com</p>
                      </div>
                    </div>
                    <div className="d-flex justify-content-between mt-3">
                      <h6 className="f-w-600 text-dark">
                        <IconDeviceMobile
                          size={18}
                          className=" me-2 text-secondary"
                        />
                        contact
                      </h6>
                      <div className="text-end">
                        <p>+1 111 134 111</p>
                      </div>
                    </div>
                  </CardBody>
                </Card>
              </Col>

              <Col lg={4}>
                <Card className="order-details-card">
                  <CardHeader>
                    <h5>Documents</h5>
                  </CardHeader>
                  <CardBody>
                    <div className="d-flex justify-content-between">
                      <h6 className="f-w-600 text-dark">
                        <IconFileInvoice
                          size={18}
                          className=" me-2 text-secondary"
                        />
                        Invoice
                      </h6>
                      <div className="text-end">
                        <p> #ABC-101011</p>
                      </div>
                    </div>
                    <div className="d-flex justify-content-between mt-3">
                      <h6 className="f-w-600 text-dark">
                        <IconTruckDelivery
                          size={18}
                          className=" me-2 text-secondary"
                        />
                        Shipping
                      </h6>
                      <div className="text-end">
                        <p>#AD-01009099</p>
                      </div>
                    </div>
                    <div className="d-flex justify-content-between mt-3">
                      <h6 className="f-w-600 text-dark">
                        <IconAward size={18} className=" me-2 text-secondary" />
                        Reward Points
                      </h6>
                      <div className="text-end">
                        <p>100</p>
                      </div>
                    </div>
                  </CardBody>
                </Card>
              </Col>
            </Row>

            <Card>
              <CardHeader>
                <h5>Order : #ACD1100897</h5>
              </CardHeader>
              <CardBody className="p-0">
                <div className="orders-details-datatable app-datatable-default app-scroll table-responsive">
                  <Table className="table-bottom-border text-center align-middle mb-0">
                    <thead>
                      <tr>
                        <th className="text-start">Product Details</th>
                        <th>Status</th>
                        <th>Price</th>
                        <th>Order Date</th>
                        <th>Quantity</th>
                        <th>Rating</th>
                        <th>Action</th>
                      </tr>
                    </thead>
                    <tbody>
                      {orders.map((order) => (
                        <tr key={order.id}>
                          <td>
                            <div className="d-flex align-items-center gap-2">
                              <img
                                src={order.productImage}
                                alt="product-img"
                                className="h-50 b-r-10"
                              />
                              <div className="text-start">
                                <h6 className="f-s-16 mb-0">
                                  {order.productName}
                                </h6>
                                <p className="text-muted f-s-13 m-0">
                                  Color:{" "}
                                  <span className="text-secondary">
                                    {order.color}
                                  </span>
                                </p>
                                <p className="text-muted f-s-13 m-0">
                                  Size:{" "}
                                  <span className="text-secondary">
                                    {order.size}
                                  </span>
                                </p>
                              </div>
                            </div>
                          </td>
                          <td>
                            <span
                              className={`badge text-light-${(() => {
                                switch (order.status) {
                                  case "CANCELLED":
                                    return "danger";
                                  case "DELIVERED":
                                    return "success";
                                  case "PICKUPS":
                                    return "info";
                                  case "RETURNS":
                                    return "secondary";
                                  default:
                                    return "light";
                                }
                              })()}`}
                            >
                              {order.status}
                            </span>
                          </td>
                          <td className="f-w-500 text-success">
                            {order.price}
                          </td>
                          <td className="f-w-500 f-s-14">{order.orderDate}</td>
                          <td>{order.quantity}</td>
                          <td>
                            <div>
                              {[...Array(5)].map((_, i) => (
                                <IconStarFilled
                                  key={i}
                                  size={16}
                                  className={
                                    i < order.rating
                                      ? "text-warning"
                                      : "text-secondary"
                                  }
                                />
                              ))}
                            </div>
                          </td>
                          <td>
                            <button className="btn icon-btn b-r-4 me-2 rounded bg-light-success">
                              <IconEdit size={18} />
                            </button>{" "}
                            <button
                              className="btn icon-btn b-r-4 rounded bg-light-danger"
                              onClick={() => handleDelete(order.id)}
                            >
                              <IconTrash size={18} />
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </Table>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col xxl={3}>
            <Card className="equal-card">
              <CardHeader>
                <h5>Order Status</h5>
              </CardHeader>
              <CardBody>
                <ul className="app-timeline-box">
                  <li className="timeline-section">
                    <div className="timeline-icon">
                      <span className="text-light-primary h-35 w-35 d-flex-center b-r-50">
                        <IconShoppingCart size={18} />
                      </span>
                    </div>
                    <div className="timeline-content bg-light-primary b-1-primary">
                      <div className="d-flex justify-content-between align-items-center timeline-flex">
                        <h6 className="mt-2 text-primary">Order Placed</h6>
                        <span className="badge text-bg-primary ms-2">
                          20 Min ago
                        </span>
                      </div>
                      <p className="mt-2 text-dark">
                        An order has been placed.
                      </p>
                      <p className="text-secondary">
                        Wed, 15 Dec 2024 - 05:34PM
                      </p>
                    </div>
                  </li>
                  <li className="timeline-section">
                    <div className="timeline-icon">
                      <span className="text-light-secondary h-35 w-35 d-flex-center b-r-50">
                        <IconChecks size={18} />
                      </span>
                    </div>
                    <div className="timeline-content bg-light-secondary b-1-secondary">
                      <div className="d-flex justify-content-between align-items-center timeline-flex">
                        <h6 className="mt-2 text-secondary">Packed</h6>
                        <span className="color-light">50 Min ago</span>
                      </div>
                      <p className="mt-2">
                        Your Item has been picked up by courier partner
                      </p>
                      <p className="text-secondary">
                        Thu, 20 Dec 2024 - 6:48AM
                      </p>
                      <span className="badge bg-primary-900">Design</span>{" "}
                      <span className="badge bg-primary-900">HTML</span>
                    </div>
                  </li>
                  <li className="timeline-section">
                    <div className="timeline-icon">
                      <span className="text-light-success h-35 w-35 d-flex-center b-r-50">
                        <IconTruckDelivery size={18} />
                      </span>
                    </div>
                    <div className="timeline-content bg-light-success b-1-success">
                      <div className="d-flex justify-content-between align-items-center timeline-flex">
                        <h6 className="mt-2 text-success">Shipping</h6>
                        <span className="badge text-bg-success ms-2">
                          1 hours ago
                        </span>
                      </div>
                      <p className="mt-2 text-dark">
                        Your Item has been picked up by courier partner
                      </p>
                      <p className="text-secondary">
                        Thu, 20 Dec 2024 - 5:48AM
                      </p>
                    </div>
                  </li>
                  <li className="timeline-section">
                    <div className="timeline-icon">
                      <span className="text-light-info h-35 w-35 d-flex-center b-r-50">
                        <IconPackage size={18} />
                      </span>
                    </div>
                    <div className="timeline-content bg-light-info b-1-info">
                      <div className="d-flex justify-content-between align-items-center timeline-flex">
                        <h6 className="mt-2 text-info">Delivered</h6>
                        <span className="badge text-bg-dark ms-2">
                          Nov 10, 14:00
                        </span>
                      </div>
                      <p className="text-secondary">
                        Mon, 26 Dec 2024 - 5:00AM
                      </p>
                    </div>
                  </li>
                </ul>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};
export default OrdersDetailsPage;
