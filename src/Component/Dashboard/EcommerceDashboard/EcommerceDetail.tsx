import React, { useEffect, useState } from "react";
import { EcommerceOrderData } from "@/Data/Charts/ApexCharts/ApexChart";
import { Card, CardBody, Col } from "reactstrap";
import Slider from "react-slick";
import { Badge } from "reactstrap";
import Loading from "@/app/loading";

const EcommerceDetail = () => {
  const [ApexCharts, setApexCharts] = useState<any>(null);

  useEffect(() => {
    const loadModules = async () => {
      const [chartModule] = await Promise.all([import("react-apexcharts")]);

      setApexCharts(() => chartModule.default || chartModule);
    };

    if (typeof window !== "undefined") {
      loadModules();
    }
  }, []);

  const orders = [
    {
      id: "P98056745",
      status: "Delivered",
      date: "October 10, 2024",
      statusClass: "success",
      badgeClass: "success",
      description: "Your order was delivered on October 10, 2024.",
    },
    {
      id: "5Qi4586781",
      status: "Shipped",
      date: "October 15, 2024",
      statusClass: "info",
      badgeClass: "info",
      description:
        "Your order has been shipped and will be delivered by October 15, 2024.",
    },
    {
      id: "84O5L6715",
      status: "Canceled",
      date: "October 14, 2024",
      statusClass: "danger",
      badgeClass: "danger",
      description: "Your order was canceled.",
    },
    {
      id: "H54367890",
      status: "Delivered",
      date: "November 30, 2024",
      statusClass: "success",
      badgeClass: "success",
      description: "Your order was delivered on November 30, 2024.",
    },
    {
      id: "78JY45672",
      status: "Shipped",
      date: "December 3, 2024",
      statusClass: "info",
      badgeClass: "info",
      description:
        "Your order has been shipped and will be delivered by December 3, 2024.",
    },
    {
      id: "45QRT9823",
      status: "Canceled",
      date: "November 28, 2024",
      statusClass: "danger",
      badgeClass: "danger",
      description: "Your order was canceled.",
    },
  ];

  const settings = {
    dots: false,
    speed: 1000,
    slidesToShow: 3,
    arrows: false,
    vertical: true,
    verticalSwiping: true,
    focusOnSelect: true,
    autoplay: true,
    autoplaySpeed: 1000,
  };
  return (
    <>
      <Col md={7} lg={5}>
        <Card>
          <CardBody className="p-0">
            <div>
              {ApexCharts ? (
                <ApexCharts
                  options={EcommerceOrderData}
                  series={EcommerceOrderData.series}
                  type="bar"
                  height={324}
                />
              ) : (
                <Loading />
              )}
            </div>
          </CardBody>
        </Card>
      </Col>
      <Col md="5" lg="4" xxl="3" className="order--1-lg">
        <Card className="order-detail-card">
          <div className="pt-3">
            <h5 className="pa-s-20">Orders details</h5>
          </div>
          <CardBody>
            <Slider {...settings}>
              {orders.map((order) => (
                <ul key={order.id} className="order-content-list">
                  <li className={`bg-${order.statusClass}-300`}>
                    <div className="d-flex align-items-center justify-content-between">
                      <h6
                        className={`text-${order.statusClass}-dark f-w-600 mb-0`}
                      >
                        {" "}
                        📦#{order.id}
                      </h6>
                      <Badge
                        color={`light-${order.statusClass}`}
                        className={`me-2 text-${order.statusClass}-dark`}
                      >
                        {order.status}
                      </Badge>
                    </div>
                    <div>
                      <p
                        className={`text-${order.statusClass} mb-0 txt-ellipsis-2`}
                      >
                        {order.description}
                      </p>
                    </div>
                    {order.status === "Canceled" && (
                      <p
                        className={`text-${order.statusClass}-dark f-w-600 mb-0 txt-ellipsis-1`}
                      >
                        <span className="f-w-600">Date Ordered</span>:{" "}
                        {order.date}
                      </p>
                    )}
                  </li>
                </ul>
              ))}
            </Slider>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default EcommerceDetail;
