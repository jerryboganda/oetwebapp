import React, { useState } from "react";
import { Badge, Card, CardBody, Col, Row, Table, Tooltip } from "reactstrap";
import { Language, Minus, Plus, StarSolid } from "iconoir-react";
import { AU, DE, CA, FR, US, ES } from "country-flag-icons/react/1x1";

interface TooltipState {
  [key: number]: boolean;
}
const TopProduct = () => {
  const topProducts = [
    {
      name: "Wireless Headphones",
      unitsSold: 250,
      sales: "$5,000",
      rating: 4.8,
    },
    { name: "Smartwatch", unitsSold: 210, sales: "$5,000", rating: 4 },
    { name: "Bluetooth Speaker", unitsSold: 190, sales: "$4,200", rating: 4.5 },
    { name: "4K Ultra HD TV", unitsSold: 175, sales: "$3,800", rating: 4.9 },
  ];
  const countriesData = [
    {
      name: "Germany",
      sales: "3.8k",
      flag: <DE className="w-30 h-30 b-r-20" />,
      cardClass: "country-card-warning",
    },
    {
      name: "Australia",
      sales: "3.8k",
      flag: <AU className="w-30 h-30 b-r-20" />,
      cardClass: "bg-primary-300",
      iconClass: "text-primary",
    },
    {
      name: "Canada",
      sales: "3.8k",
      flag: <CA className="w-30 h-30 b-r-20" />,
      cardClass: "country-card-danger",
    },
    {
      name: "France",
      sales: "3.8k",
      flag: <FR className="w-30 h-30 b-r-20" />,
      cardClass: "country-card-info",
    },
    {
      name: "USA",
      sales: "3.8k",
      flag: <US className="w-30 h-30 b-r-20" />,
      cardClass: "country-card-danger",
    },
    {
      name: "Spain",
      sales: "3.8k",
      flag: <ES className="w-30 h-30 b-r-20" />,
      cardClass: "country-card-warning",
    },
  ];
  const customerData = [
    {
      id: 1,
      name: "Emily Johnson",
      avatar: "D",
      status: "Added",
      iconClass: <Plus height={16} width={16} className="f-s-20" />,
      tooltip: "Added",
    },
    {
      id: 2,
      name: "Emily Johnson",
      avatar: "AD",
      status: "Removed",
      iconClass: <Minus height={16} width={16} className="f-s-20" />,
      tooltip: "Removed",
    },
    {
      id: 3,
      name: "Emily Johnson",
      avatar: "AD",
      status: "Added",
      iconClass: <Plus height={16} width={16} className="f-s-20" />,
      tooltip: "Added",
    },
    {
      id: 4,
      name: "Emily Johnson",
      avatar: "AD",
      status: "Added",
      iconClass: <Plus height={16} width={16} className="f-s-20" />,
      tooltip: "Added",
    },
    {
      id: 5,
      name: "Emily Johnson",
      avatar: "AD",
      status: "Removed",
      iconClass: <Minus height={16} width={16} className="f-s-20" />,
      tooltip: "Removed",
    },
  ];
  const [tooltipOpen, setTooltipOpen] = useState<TooltipState>({});

  const toggleTooltip = (id: number) => {
    setTooltipOpen((prevState) => ({
      ...prevState,
      [id]: !prevState[id],
    }));
  };

  const handleButtonClick = (index: number) => {
    const newCustomerData = [...customerData];
    const customer = newCustomerData[index];

    if (customer) {
      if (customer.status === "Added") {
        customer.status = "Removed";
        customer.iconClass = (
          <Minus height={16} width={16} className="f-s-20" />
        );
        customer.tooltip = "Removed";
      } else {
        customer.status = "Added";
        customer.iconClass = <Plus height={16} width={16} className="f-s-20" />;
        customer.tooltip = "Added";
      }
      customerData[index] = customer;
    }
  };

  return (
    <>
      <Col lg="7" xxl="4">
        <div className="p-3">
          <h5>Top List Products</h5>
        </div>
        <Card>
          <CardBody className="px-0">
            <div className="table-responsive app-scroll">
              <Table className="align-middle top-products-table mb-0">
                <thead>
                  <tr>
                    <th scope="col">Product</th>
                    <th scope="col">Units Sold</th>
                    <th scope="col">Sales</th>
                    <th scope="col">Rating</th>
                  </tr>
                </thead>
                <tbody>
                  {topProducts.map((product, index) => (
                    <tr key={index}>
                      <td>
                        <div className="d-flex align-items-center">
                          <h6 className="mb-0">{product.name}</h6>
                        </div>
                      </td>
                      <td>
                        <Badge
                          color={
                            product.unitsSold > 200
                              ? "light-success"
                              : product.unitsSold > 150
                                ? "light-primary"
                                : "light-danger"
                          }
                          className="f-s-12 f-w-700"
                        >
                          {product.unitsSold}
                        </Badge>
                      </td>
                      <td className="f-w-600 text-dark">{product.sales}</td>
                      <td className="text-warning-dark f-w-600">
                        <StarSolid
                          height={16}
                          width={16}
                          className="text-warning me-1"
                        />
                        {product.rating}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </Table>
            </div>
          </CardBody>
        </Card>
      </Col>
      <Col md="7" xxl="5">
        <div className="p-3">
          <h5>Sales by Country</h5>
        </div>
        <Row>
          {countriesData.map((country, index) => (
            <Col key={index} xs="6" sm="4">
              <Card className={country.cardClass}>
                <CardBody>
                  {country.flag}
                  <div className="mt-3">
                    <h6 className="mb-0">{country.name}</h6>
                    <p className="f-w-600 mb-0">{country.sales}</p>
                  </div>
                  <Language
                    height={70}
                    width={70}
                    className="icon-bg text-white"
                  />
                  {country.iconClass && (
                    <span
                      className={`position-absolute top-0 end-0 pa-6 bg-success b-2-white border-light rounded-circle animate__animated animate__heartBeat animate__infinite animate__fast`}
                    ></span>
                  )}
                </CardBody>
              </Card>
            </Col>
          ))}
        </Row>
      </Col>
      <Col md="5" xxl="3">
        <div className="p-3">
          <h5>Customer</h5>
        </div>
        <Card>
          <CardBody>
            <ul className="customer-list">
              {customerData.map((customer, index) => (
                <li key={customer.id} className="customer-list-item">
                  <span
                    className={`text-light-primary f-w-600 h-35 w-35 d-flex-center b-r-50 customer-list-avatar`}
                  >
                    {customer.avatar}
                  </span>
                  <div className="customer-list-content">
                    <h6 className="mb-0">{customer.name}</h6>
                  </div>
                  <div>
                    <button
                      id={`toggleCustomerButton-${customer.id}`}
                      className={`toggleCustomerButton text-light-${customer.status === "Added" ? "primary" : "danger"} h-35 w-35 d-flex-center b-r-50`}
                      onClick={() => handleButtonClick(index)}
                      title={customer.tooltip}
                    >
                      {customer.iconClass}
                    </button>
                    <Tooltip
                      isOpen={tooltipOpen[customer.id] ?? false}
                      target={`toggleCustomerButton-${customer.id}`}
                      toggle={() => toggleTooltip(customer.id)}
                    >
                      {customer.tooltip}
                    </Tooltip>
                  </div>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default TopProduct;
