"use client";
import React, { useState } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { Button, Card, CardBody, Col, Container, Row, Table } from "reactstrap";
import { orders } from "@/Data/Apps/Orderpage/Orderpage";
import Link from "next/link";
import {
  IconArrowBack,
  IconEdit,
  IconEye,
  IconSortDescending2,
  IconSquareRoundedX,
  IconStack2,
  IconTrash,
  IconTruckDelivery,
  IconTruckLoading,
} from "@tabler/icons-react";

const ProductDetailsPage = () => {
  const [activeTab, setActiveTab] = useState("connect-tab");
  const handleTabClick = (tab: string) => {
    setActiveTab(tab);
  };

  // Filter orders by status for each tab
  const allOrders = orders;
  const deliveredOrders = orders.filter(
    (order) => order.status === "DELIVERED"
  );
  const pickupOrders = orders.filter((order) => order.status === "PICKUPS");
  const returnOrders = orders.filter((order) => order.status === "RETURNS");
  const cancelledOrders = orders.filter(
    (order) => order.status === "CANCELLED"
  );

  const renderTable = (data: typeof orders) => {
    return (
      <div className="order-list-table table-responsive app-scroll">
        <Table className="table-bottom-border align-middle mb-0">
          <thead>
            <tr>
              <th>
                <label className="check-box">
                  <input type="checkbox" id="select-all" />
                  <span className="checkmark outline-secondary ms-2 "></span>
                </label>
              </th>
              <th>Order Id</th>
              <th scope="col" className="text-start">
                Customer
              </th>
              <th scope="col">Product</th>
              <th scope="col">Status</th>
              <th scope="col">Order Date</th>
              <th scope="col">Payment Method</th>
              <th scope="col">Amount</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            {data.map((order, index) => (
              <tr key={index}>
                <td>
                  <label className="check-box">
                    <input type="checkbox" />
                    <span className="checkmark outline-secondary ms-2"></span>
                  </label>
                </td>
                <td>{order.id}</td>
                <td className="d-flex align-items-center gap-2">
                  <div className="h-25 w-25 d-flex-center b-r-50 overflow-hidden text-bg-primary">
                    <img
                      src={order.customer.avatar}
                      alt=""
                      className="img-fluid"
                    />
                  </div>
                  <span className="title-text mb-0">{order.customer.name}</span>
                </td>
                <td>{order.product}</td>
                <td>
                  <span
                    className={`badge text-light-${(() => {
                      if (order.status === "CANCELLED") return "danger";
                      if (order.status === "DELIVERED") return "success";
                      if (order.status === "INPROGRESS") return "warning";
                      if (order.status === "PICKUPS") return "info";
                      if (order.status === "RETURNS") return "secondary";
                      return "light";
                    })()}`}
                  >
                    {order.status}
                  </span>
                </td>
                <td>{order.orderDate}</td>
                <td>{order.paymentMethod}</td>
                <td>{order.amount}</td>
                <td>
                  <Link
                    href="/apps/e-shop/orders-details"
                    role="button"
                    target="_blank"
                    rel="noopener noreferrer"
                    className="btn btn-outline-primary icon-btn w-30 h-30 b-r-22 me-2"
                  >
                    <IconEye size={18} />
                  </Link>
                  <Button
                    color="outline-success"
                    className="icon-btn w-30 h-30 b-r-22 me-2"
                  >
                    <IconEdit size={18} />
                  </Button>
                  <Button
                    color="outline-danger"
                    className="icon-btn w-30 h-30 b-r-22 me-2"
                  >
                    <IconTrash size={18} />
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </Table>
      </div>
    );
  };

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Orders"
        title="Apps"
        path={["E-shop", "Orders"]}
        Icon={IconStack2}
      />
      <Row>
        <Col xs={12}>
          <Card>
            <CardBody>
              <ul
                className="nav nav-tabs app-tabs-primary order-tabs d-flex justify-content-start border-0 mb-0 pb-0"
                id="Outline"
                role="tablist"
              >
                <li className="nav-item" role="presentation">
                  <button
                    className={`nav-link d-flex align-items-center gap-1 ${
                      activeTab === "connect-tab" ? "active" : ""
                    }`}
                    type="button"
                    onClick={() => handleTabClick("connect-tab")}
                  >
                    <IconSortDescending2 size={18} className="mg-b-3" /> All
                    Orders
                  </button>
                </li>
                <li className="nav-item" role="presentation">
                  <button
                    className={`nav-link d-flex align-items-center gap-1 ${
                      activeTab === "discover-tab" ? "active" : ""
                    }`}
                    type="button"
                    onClick={() => handleTabClick("discover-tab")}
                  >
                    <IconTruckDelivery size={18} className="mg-b-3" /> Delivered
                  </button>
                </li>
                <li className="nav-item" role="presentation">
                  <button
                    className={`nav-link d-flex align-items-center gap-1 ${
                      activeTab === "order-tab" ? "active" : ""
                    }`}
                    type="button"
                    onClick={() => handleTabClick("order-tab")}
                  >
                    <IconTruckLoading size={18} className="mg-b-3" /> Pickups
                  </button>
                </li>
                <li className="nav-item" role="presentation">
                  <button
                    className={`nav-link d-flex align-items-center gap-1 ${
                      activeTab === "order-tabs" ? "active" : ""
                    }`}
                    type="button"
                    onClick={() => handleTabClick("order-tabs")}
                  >
                    <IconArrowBack size={18} className="mg-b-3" /> Returns
                  </button>
                </li>
                <li className="nav-item" role="presentation">
                  <button
                    className={`nav-link d-flex align-items-center gap-1 ${
                      activeTab === "ordertab" ? "active" : ""
                    }`}
                    type="button"
                    onClick={() => handleTabClick("ordertab")}
                  >
                    <IconSquareRoundedX size={18} className="mg-b-3" />{" "}
                    Cancelled
                  </button>
                </li>
              </ul>
            </CardBody>

            <div className="card-body order-tab-content p-0">
              <div className="tab-content" id="OutlineContent">
                {activeTab === "connect-tab" && (
                  <div className="tab-pane active show">
                    {renderTable(allOrders)}
                  </div>
                )}
                {activeTab === "discover-tab" && (
                  <div className="tab-pane active show">
                    {renderTable(deliveredOrders)}
                  </div>
                )}
                {activeTab === "order-tab" && (
                  <div className="tab-pane active show">
                    {renderTable(pickupOrders)}
                  </div>
                )}
                {activeTab === "order-tabs" && (
                  <div className="tab-pane active show">
                    {renderTable(returnOrders)}
                  </div>
                )}
                {activeTab === "ordertab" && (
                  <div className="tab-pane active show">
                    {renderTable(cancelledOrders)}
                  </div>
                )}
              </div>
            </div>
          </Card>
        </Col>
      </Row>
    </Container>
  );
};

export default ProductDetailsPage;
