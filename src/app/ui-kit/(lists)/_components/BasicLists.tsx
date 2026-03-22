import React, { useState } from "react";
import {
  Container,
  Row,
  Col,
  Card,
  CardHeader,
  CardBody,
  ListGroup,
  ListGroupItem,
} from "reactstrap";
import {
  activeList,
  buttonList,
  defaultList,
  horizontalList,
  linkList,
  numberedList,
} from "@/Data/UiKit/ListData/listPageData";
import { IconArrowAutofitRight, IconUnlink } from "@tabler/icons-react";

const ListGroupComponent: React.FC = () => {
  const [activeIndex, setActiveIndex] = useState<number | null>(0);

  const handleClick = (index: number) => {
    setActiveIndex(activeIndex === index ? null : index); // Toggle active state
  };

  return (
    <Container>
      <Row>
        <Col md={6} xl={4}>
          <Card>
            <CardHeader>
              <h5>Default Lists</h5>
            </CardHeader>
            <CardBody>
              <ListGroup>
                {defaultList.map((item, index) => (
                  <ListGroupItem key={index}>{item}</ListGroupItem>
                ))}
              </ListGroup>
            </CardBody>
          </Card>
        </Col>

        <Col md={6} xl={4}>
          <Card>
            <CardHeader>
              <h5>Active Items</h5>
            </CardHeader>
            <CardBody>
              <ListGroup className="list-items-active">
                {activeList.map((item, index) => (
                  <ListGroupItem
                    key={item.id}
                    active={activeIndex === index}
                    className={`list-active ${activeIndex === index ? "active" : ""}`}
                    onClick={() => handleClick(index)}
                  >
                    {item.text}
                  </ListGroupItem>
                ))}
              </ListGroup>
            </CardBody>
          </Card>
        </Col>

        <Col md={6} xl={4}>
          <Card>
            <CardHeader>
              <h5>Links</h5>
            </CardHeader>
            <CardBody>
              <ListGroup className="list-group list-link">
                {linkList.map((link) => (
                  <a
                    key={link.id}
                    href={link.disabled ? undefined : link.href}
                    className={`list-group-item list-group-item-action ${
                      link.active ? "active" : ""
                    } ${link.disabled ? "disabled" : ""}`}
                    aria-disabled={link.disabled}
                  >
                    <IconUnlink size={16} className="me-2" />
                    {link.text}
                  </a>
                ))}
              </ListGroup>
            </CardBody>
          </Card>
        </Col>

        <Col md={6} xl={4}>
          <Card>
            <CardHeader className="code-header">
              <h5>Buttons</h5>
            </CardHeader>
            <CardBody className="gap-2 d-flex flex-column">
              <div className="list-group list-button">
                {buttonList.map((button) => (
                  <button
                    key={button.id}
                    type="button"
                    className={`list-group-item list-group-item-action ${
                      button.active ? "active" : ""
                    }`}
                    disabled={button.disabled}
                  >
                    <IconArrowAutofitRight size={18} className="me-2" />
                    {button.text}
                  </button>
                ))}
              </div>
            </CardBody>
          </Card>
        </Col>

        <Col md={6} xl={4}>
          <Card>
            <CardHeader className="code-header">
              <h5>Numbered</h5>
            </CardHeader>
            <CardBody>
              <ol className="list-group list-group-numbered p-1">
                {numberedList.map((item) => (
                  <li
                    key={item.id}
                    className={`list-group-item d-flex justify-content-between align-items-start text-${item.color}`}
                  >
                    <div className="ms-2 w-100">
                      <div className="w-100 d-flex justify-content-between align-items-center">
                        <div className="fw-bold me-1">Subheading</div>
                        <span
                          className={`badge text-light-${item.color} rounded-pill`}
                        >
                          {item.count}
                        </span>
                      </div>
                      {item.text}
                    </div>
                  </li>
                ))}
              </ol>
            </CardBody>
          </Card>
        </Col>

        <Col md={6} xl={4}>
          <Card>
            <CardHeader className="code-header">
              <h5>Horizontal</h5>
            </CardHeader>
            <CardBody className="list-horizontal gap-2 d-flex flex-column align-items-center">
              {horizontalList.map((group) => (
                <ul key={group.id} className="list-group list-group-horizontal">
                  {group.items.map((item, index) => (
                    <li
                      key={index}
                      className={`list-group-item ${group.className}`}
                    >
                      {item}
                    </li>
                  ))}
                </ul>
              ))}
            </CardBody>
          </Card>
        </Col>
      </Row>
    </Container>
  );
};

export default ListGroupComponent;
