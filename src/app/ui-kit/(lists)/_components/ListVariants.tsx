import React from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Col,
  ListGroup,
  ListGroupItem,
} from "reactstrap";
import { usersList, variantList } from "@/Data/UiKit/ListData/listPageData";

const ListVariants = () => {
  return (
    <>
      <Col lg={6}>
        <Card>
          <CardHeader>
            <h5>Variants</h5>
          </CardHeader>
          <CardBody>
            <ListGroup>
              {variantList.map((item, index) => (
                <ListGroupItem key={index} className={item.className}>
                  {item.text}
                </ListGroupItem>
              ))}
            </ListGroup>
          </CardBody>
        </Card>
      </Col>

      {/* Custom Content List */}
      <Col lg={6}>
        <Card>
          <CardHeader>
            <h5>Custom Content</h5>
          </CardHeader>
          <CardBody>
            <ListGroup className="list-group list-content">
              {usersList.map((user, index) => (
                <ListGroupItem
                  key={user.id}
                  className={`list-group-item list-group-item-action ${index === 0 ? "active" : ""}`}
                  aria-current={index === 0 ? "true" : "false"}
                >
                  <div className="position-absolute">
                    <span className="bg-secondary h-45 w-45 d-flex-center b-r-50 position-relative">
                      <img
                        src={user.avatar}
                        alt={user.name}
                        className="img-fluid b-r-50"
                      />
                      {user.status === "online" && (
                        <span className="position-absolute top-0 end-0 p-1 bg-success border border-light rounded-circle"></span>
                      )}
                      {user.status === "unread" && (
                        <span className="position-absolute top-0 end-0 d-flex-center bg-warning border-light rounded-circle text-center h-20 w-20 f-s-10 start-30">
                          {user.unreadCount}
                        </span>
                      )}
                      {user.status === "new-message" && (
                        <span className="position-absolute top-0 d-flex-center bg-danger border border-light rounded-circle text-center h-20 w-20 f-s-10 start-30">
                          <i className="ti ti-mail"></i>
                        </span>
                      )}
                    </span>
                  </div>
                  <div className="mg-s-60">
                    <h6 className="mb-0">{user.name}</h6>
                    <p className="mb-0 text-secondary">{user.email}</p>
                    <div className="mt-2">
                      <p className="mb-0">{user.message}</p>
                    </div>
                  </div>
                  <div className="flex-shrink-0">
                    <small>{user.time}</small>
                  </div>
                </ListGroupItem>
              ))}
            </ListGroup>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default ListVariants;
